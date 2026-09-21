using System;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Action;
using Vortex.Primitives.Feed;

namespace Vortex.Dashboard.API.Operations.Hotel;

/// <summary>
/// Taking a post down from the website's social feed.
/// </summary>
/// <remarks>
/// Through the runner like every other operator write, and that is the point of the class rather
/// than calling the service straight from the route: the runner is what records who did it, to
/// what, and why. A moderation action on a player's own words is exactly the kind that has to
/// leave a trace, and it would be the only write in this console that does not.
/// </remarks>
internal sealed class FeedOperations(OperationRunner runner, IFeedAdminService feedAdmin)
{
    private readonly OperationRunner _runner = runner;
    private readonly IFeedAdminService _feedAdmin = feedAdmin;

    public Task<OperationResult> RemovePostAsync(
        int postId,
        string reason,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.feed.post.remove",
            actor,
            reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { postId },
            work: async c =>
            {
                if (!await _feedAdmin.RemoveAsync(postId, c).ConfigureAwait(false))
                {
                    // Thrown rather than returned so the runner records the attempt and its
                    // outcome: an operator aiming at a post that is already gone is worth the same
                    // line in the journal as one that lands.
                    throw new InvalidOperationException("feed_post_not_found");
                }
            },
            ct
        );
}
