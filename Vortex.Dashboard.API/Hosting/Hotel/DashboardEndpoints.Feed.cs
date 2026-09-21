using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Vortex.Dashboard.API.Operations.Hotel;
using Vortex.Dashboard.API.Operations.Hotel.Contracts;
using Vortex.Primitives.Permissions;

namespace Vortex.Dashboard.API.Hosting;

/// <summary>
/// The operator's half of the website's social feed.
/// </summary>
/// <remarks>
/// One route: taking a post down. A player deletes their own on the website itself, which checks
/// ownership; this one does not, and that is exactly why it lives here — <c>Vortex.WebApi</c> has
/// no permission check anywhere in it, and this console already has the whole model.
/// </remarks>
internal static partial class DashboardEndpoints
{
    public static void MapFeedOperations(WebApplication app)
    {
        MapPost(
            app,
            ApiOperations + "/feed/remove",
            async (
                HttpContext ctx,
                RemoveFeedPostRequest body,
                FeedOperations ops,
                CancellationToken ct
            ) =>
                body.PostId <= 0
                    ? Results.BadRequest(new { error = "invalid_request" })
                    : Results.Ok(
                        await ops.RemovePostAsync(body.PostId, body.Reason, ctx.ActorEmail(), ct)
                            .ConfigureAwait(false)
                    ),
            Capabilities.Dashboard.OpsFeedModerate,
            TagOperations
        );
    }
}
