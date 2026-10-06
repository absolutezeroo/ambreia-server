using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Vortex.Primitives.Authentication;
using Vortex.WebApi.Http;
using Vortex.WebApi.Notifications;
using Vortex.WebApi.Services;
using Vortex.WebApi.Session;

namespace Vortex.WebApi.Hosting;

/// <summary>
/// The website's social feed.
/// </summary>
/// <remarks>
/// The reads are anonymous, like the profile: a feed is a page a signed-out
/// visitor opens. The session is still read, because it decides the "…ByMe" fields and nothing
/// else.
/// </remarks>
internal static partial class WebApiEndpointsFeed
{
    public static void MapFeed(WebApplication app, string tagPublic, string tagUser)
    {
        app.MapGet(
                "/api/public/feed",
                async Task<Ok<FeedPage>> (
                    HttpContext ctx,
                    int? page,
                    int? pageSize,
                    string? tag,
                    // [FromServices] rather than left to inference: an unknown service is inferred
                    // as a request body, a GET may not have one, and the whole route table then
                    // fails to build.
                    [FromServices] IWebApiFeedService feed,
                    [FromServices] WebApiSessionStore sessions,
                    [FromServices] IWebApiPlayerService players,
                    CancellationToken ct
                ) =>
                    TypedResults.Ok(
                        await feed.GetFeedAsync(
                                page ?? 1,
                                pageSize ?? 0,
                                tag,
                                (
                                    await WebApiEndpoints
                                        .SelectedPlayerAsync(ctx, sessions, players, ct)
                                        .ConfigureAwait(false)
                                ).PlayerId,
                                ct
                            )
                            .ConfigureAwait(false)
                    )
            )
            .WithName("Feed")
            .WithSummary("The social feed: every post, newest first.")
            .WithTags(tagPublic);

        app.MapGet(
                "/api/public/feed/{id:int}",
                async Task<Results<Ok<FeedPost>, NotFound<ApiErrorResponse>>> (
                    HttpContext ctx,
                    int id,
                    [FromServices] IWebApiFeedService feed,
                    [FromServices] WebApiSessionStore sessions,
                    [FromServices] IWebApiPlayerService players,
                    CancellationToken ct
                ) =>
                {
                    (int? viewerId, _) = await WebApiEndpoints
                        .SelectedPlayerAsync(ctx, sessions, players, ct)
                        .ConfigureAwait(false);

                    FeedPost? post = await feed.GetPostAsync(id, viewerId, ct)
                        .ConfigureAwait(false);

                    return post is null
                        ? TypedResults.NotFound(new ApiErrorResponse("feed_post_not_found"))
                        : TypedResults.Ok(post);
                }
            )
            .WithName("FeedPost")
            .WithSummary("One post and its comments.")
            .WithTags(tagPublic);

        // Anonymous, like the feed's other reads: a signed-out visitor watching the page should
        // see it move too. It carries only the id of the thread that changed, never the thread —
        // a post's "…ByMe" fields are per-reader, so one pushed copy would be right for the
        // person who caused it and wrong for everybody else. Each reader refetches that one card
        // and gets their own answer.
        //
        // Same deployment note as the notification stream: nginx needs `proxy_buffering off`, and
        // the per-frame flush below is what makes the rest of it arrive at all.
        app.MapGet(
                "/api/public/feed/stream",
                async Task (
                    HttpContext ctx,
                    [FromServices] FeedBroadcaster feed,
                    CancellationToken ct
                ) =>
                {
                    ctx.Response.Headers.ContentType = "text/event-stream";
                    ctx.Response.Headers.CacheControl = "no-cache";
                    ctx.Response.Headers["X-Accel-Buffering"] = "no";

                    using FeedBroadcaster.Subscription subscription = feed.Subscribe();

                    // An opening frame carrying no id. Without it a proxy cannot tell the response
                    // has begun, and neither can the browser — EventSource stays "connecting"
                    // until the first byte, which may be minutes away on a quiet hotel.
                    await WriteFeedAsync(ctx, "ready", ct).ConfigureAwait(false);

                    try
                    {
                        await foreach (
                            int rootId in subscription.Reader.ReadAllAsync(ct).ConfigureAwait(false)
                        )
                        {
                            await WriteFeedAsync(
                                    ctx,
                                    rootId.ToString(CultureInfo.InvariantCulture),
                                    ct
                                )
                                .ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // The tab closed. The only way this loop ends, and the `using` is what
                        // actually unsubscribes.
                    }
                }
            )
            .WithName("FeedStream")
            .WithSummary("Which thread just changed, as server-sent events.")
            .WithTags(tagPublic);

        app.MapPost(
                "/api/user/feed",
                async Task<
                    Results<
                        Ok<FeedPost>,
                        BadRequest<ApiErrorResponse>,
                        NotFound<ApiErrorResponse>,
                        InternalServerError<ApiErrorResponse>,
                        UnauthorizedError
                    >
                > (
                    HttpContext ctx,
                    FeedPublishRequest body,
                    [FromServices] IWebApiFeedService feed,
                    [FromServices] WebApiSessionStore sessions,
                    [FromServices] IWebApiPlayerService players,
                    CancellationToken ct
                ) =>
                {
                    (int? playerId, IResult? refused) = await WebApiEndpoints
                        .SelectedPlayerAsync(ctx, sessions, players, ct)
                        .ConfigureAwait(false);

                    if (playerId is null)
                    {
                        // No session, or a session with no avatar: a post belongs to a PLAYER, and
                        // an account without one has nobody to attribute it to.
                        return refused is UnauthorizedError unauthorized
                            ? unauthorized
                            : TypedResults.NotFound(new ApiErrorResponse("pocket.auth.no_avatars"));
                    }

                    (FeedRefusal refusal, FeedPost? post) = await feed.PublishAsync(
                            playerId.Value,
                            body,
                            ct
                        )
                        .ConfigureAwait(false);

                    if (refusal == FeedRefusal.None)
                    {
                        // PublishAsync's contract is that None comes with the post it just wrote.
                        // A null here is a broken invariant, not a refusal the caller sent — answer
                        // it as one rather than serving a 200 with a null body no client expects.
                        return post is null
                            ? TypedResults.InternalServerError(new ApiErrorResponse("internal"))
                            : TypedResults.Ok(post);
                    }

                    return refusal switch
                    {
                        FeedRefusal.ParentNotFound => TypedResults.NotFound(
                            new ApiErrorResponse("feed_post_not_found")
                        ),
                        _ => TypedResults.BadRequest(new ApiErrorResponse(Code(refusal))),
                    };
                }
            )
            // Rate-limited, and sharing its budget with the delete below. See
            // WebApiConfig.FeedPostRateLimit for why the votes and reactions beside it are not.
            .RequireRateLimiting(WebApiEndpoints.FeedPostRateLimitPolicy)
            .WithName("FeedPublish")
            .WithSummary("Publish a post, or a comment on one.")
            .WithTags(tagUser);

        app.MapPost(
                "/api/user/feed/{id:int}/vote",
                async Task<Results<Ok<FeedPost>, NotFound<ApiErrorResponse>, UnauthorizedError>> (
                    HttpContext ctx,
                    int id,
                    FeedVoteRequest body,
                    [FromServices] IWebApiFeedService feed,
                    [FromServices] WebApiSessionStore sessions,
                    [FromServices] IWebApiPlayerService players,
                    CancellationToken ct
                ) =>
                {
                    (int? playerId, IResult? refused) = await WebApiEndpoints
                        .SelectedPlayerAsync(ctx, sessions, players, ct)
                        .ConfigureAwait(false);

                    if (playerId is null)
                    {
                        // No session, or a session with no avatar: a vote belongs to a PLAYER, and
                        // an account without one has nobody to attribute it to.
                        return refused is UnauthorizedError unauthorized
                            ? unauthorized
                            : TypedResults.NotFound(new ApiErrorResponse("pocket.auth.no_avatars"));
                    }

                    FeedPost? post = await feed.VoteAsync(playerId.Value, id, body.ChoiceId, ct)
                        .ConfigureAwait(false);

                    // One 404 for three causes — no such poll, deleted, or a choice from a
                    // different one. Distinguishing them would tell a caller which ids exist, and
                    // the page has nothing different to do with the answer either way.
                    return post is null
                        ? TypedResults.NotFound(new ApiErrorResponse("feed_post_not_found"))
                        : TypedResults.Ok(post);
                }
            )
            .WithName("FeedVote")
            .WithSummary("Vote in a poll, or move an existing vote.")
            .WithTags(tagUser);

        // PUT, not POST: this sets a state rather than appending an event, and the request is
        // idempotent by construction — sending it twice leaves the same one row.
        app.MapPut(
                "/api/user/feed/{id:int}/reaction",
                async Task<Results<Ok<FeedPost>, NotFound<ApiErrorResponse>, UnauthorizedError>> (
                    HttpContext ctx,
                    int id,
                    FeedReactionRequest body,
                    [FromServices] IWebApiFeedService feed,
                    [FromServices] WebApiSessionStore sessions,
                    [FromServices] IWebApiPlayerService players,
                    CancellationToken ct
                ) =>
                {
                    (int? playerId, IResult? refused) = await WebApiEndpoints
                        .SelectedPlayerAsync(ctx, sessions, players, ct)
                        .ConfigureAwait(false);

                    if (playerId is null)
                    {
                        // No session, or a session with no avatar: a reaction belongs to a PLAYER,
                        // and an account without one has nobody to attribute it to.
                        return refused is UnauthorizedError unauthorized
                            ? unauthorized
                            : TypedResults.NotFound(new ApiErrorResponse("pocket.auth.no_avatars"));
                    }

                    FeedPost? post = await feed.SetReactionAsync(
                            playerId.Value,
                            id,
                            body.Kind,
                            body.On,
                            ct
                        )
                        .ConfigureAwait(false);

                    return post is null
                        ? TypedResults.NotFound(new ApiErrorResponse("feed_post_not_found"))
                        : TypedResults.Ok(post);
                }
            )
            .WithName("FeedReaction")
            .WithSummary("Leave or withdraw a repost or a favourite.")
            .WithTags(tagUser);

        app.MapDelete(
                "/api/user/feed/{id:int}",
                async Task<
                    Results<
                        Ok<EmptyResponse>,
                        BadRequest<ApiErrorResponse>,
                        NotFound<ApiErrorResponse>,
                        UnauthorizedError
                    >
                > (
                    HttpContext ctx,
                    int id,
                    // The second factor, when the account has one. On the query string because a
                    // DELETE carries no body every intermediary is obliged to forward — and a TOTP
                    // is good for one 30-second window and one use, so what an access log keeps of
                    // it is already spent by the time anyone reads it.
                    string? code,
                    [FromServices] IWebApiFeedService feed,
                    [FromServices] WebApiSessionStore sessions,
                    [FromServices] IWebApiPlayerService players,
                    [FromServices] IAccountMfaService mfa,
                    CancellationToken ct
                ) =>
                {
                    (int? playerId, IResult? refused) = await WebApiEndpoints
                        .SelectedPlayerAsync(ctx, sessions, players, ct)
                        .ConfigureAwait(false);

                    if (playerId is null)
                    {
                        return refused is UnauthorizedError unauthorized
                            ? unauthorized
                            : TypedResults.NotFound(new ApiErrorResponse("pocket.auth.no_avatars"));
                    }

                    // Deleting is the one thing on the feed that cannot be undone — the post goes
                    // and its comments, other people's included, go with it — so it is the one
                    // thing here that asks an account with a second factor to prove itself. The
                    // reads, the votes and the reactions do not: a stolen session that can vote is
                    // a nuisance, one that can erase a thread is not.
                    //
                    // The gate sits on the route and not in the feed service because a post
                    // belongs to a PLAYER and a second factor to an ACCOUNT, and this is the only
                    // place holding both. SelectedPlayerAsync has already refused a request with
                    // no session, so the account id behind it resolves.
                    int accountId = ctx.AccountId(sessions)!.Value;

                    if (await mfa.IsEnabledAsync(accountId, ct).ConfigureAwait(false))
                    {
                        if (string.IsNullOrWhiteSpace(code))
                        {
                            return TypedResults.BadRequest(
                                new ApiErrorResponse("pocket.auth.mfa_required")
                            );
                        }

                        if (!await mfa.VerifyAsync(accountId, code, ct).ConfigureAwait(false))
                        {
                            return TypedResults.BadRequest(
                                new ApiErrorResponse("pocket.auth.invalid_code")
                            );
                        }
                    }

                    // One answer for "no such post", "already gone" and "not yours". Telling them
                    // apart would let a caller probe which ids exist and who wrote them, and the
                    // page does the same thing with all three: stop showing the card.
                    return await feed.DeleteAsync(playerId.Value, id, ct).ConfigureAwait(false)
                        ? TypedResults.Ok(new EmptyResponse())
                        : TypedResults.NotFound(new ApiErrorResponse("feed_post_not_found"));
                }
            )
            // Rate-limited, for the same reason it takes a code at all: six digits is a few
            // thousand requests, and an unthrottled gate would cost an attacker the time it takes
            // to send them and nothing else. The publish policy is reused rather than a second one
            // declared — both are "this session, a handful of writes a minute".
            .RequireRateLimiting(WebApiEndpoints.FeedPostRateLimitPolicy)
            .WithName("FeedDelete")
            .WithSummary("Delete one of your own posts, and its comments with it.")
            .WithTags(tagUser);
    }

    /// <summary>
    /// One SSE frame, flushed immediately — without the flush it sits in Kestrel's buffer until
    /// the request ends, and this request only ends when the tab closes.
    /// </summary>
    private static async Task WriteFeedAsync(HttpContext ctx, string data, CancellationToken ct)
    {
        await ctx.Response.WriteAsync($"data: {data}\n\n", ct).ConfigureAwait(false);
        await ctx.Response.Body.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// A refusal to the code the website words. One code per reason rather than a single
    /// "invalid": the composer has three tabs and a reader has to be told which one is wrong.
    /// </summary>
    private static string Code(FeedRefusal refusal) =>
        refusal switch
        {
            FeedRefusal.BodyEmpty => "feed_body_empty",
            FeedRefusal.BodyTooLong => "feed_body_too_long",
            FeedRefusal.PollInvalid => "feed_poll_invalid",
            FeedRefusal.ParentNotRoot => "feed_parent_not_root",
            FeedRefusal.GroupNotMember => "feed_group_not_member",
            FeedRefusal.KindMismatch => "feed_kind_mismatch",
            FeedRefusal.WordFiltered => "feed_word_filtered",
            _ => "feed_invalid",
        };
}
