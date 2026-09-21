using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Vortex.WebApi.Http;
using Vortex.WebApi.Services;
using Vortex.WebApi.Session;

namespace Vortex.WebApi.Hosting;

/// <summary>
/// The website's social feed.
/// </summary>
/// <remarks>
/// The reads are anonymous, like the photo gallery and the profile: a feed is a page a signed-out
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
            FeedRefusal.TooManyPhotos => "feed_too_many_photos",
            FeedRefusal.PhotoNotYours => "feed_photo_not_yours",
            FeedRefusal.PollInvalid => "feed_poll_invalid",
            FeedRefusal.ParentNotRoot => "feed_parent_not_root",
            FeedRefusal.GroupNotMember => "feed_group_not_member",
            FeedRefusal.KindMismatch => "feed_kind_mismatch",
            _ => "feed_invalid",
        };
}
