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
    public static void MapFeed(WebApplication app, string tagPublic)
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
    }
}
