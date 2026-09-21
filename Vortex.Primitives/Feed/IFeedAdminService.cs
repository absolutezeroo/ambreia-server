using System.Threading;
using System.Threading.Tasks;

namespace Vortex.Primitives.Feed;

/// <summary>
/// The operator's half of deleting from the website's social feed.
/// </summary>
/// <remarks>
/// <para>
/// A player takes their own post back through the website
/// (<c>DELETE /api/user/feed/{id}</c>, which checks ownership); this is the takedown that does not,
/// and it is reachable only through a dashboard route carrying
/// <c>Capabilities.Dashboard.OpsFeedModerate</c>. The capability is checked there, once, by the
/// route — never in the implementation, where a second copy of the rule would be a second thing to
/// get wrong.
/// </para>
/// <para>
/// It lives on the dashboard rather than on the public web API because <c>Vortex.WebApi</c> has no
/// permission check anywhere in it, and teaching it one for a single endpoint would put the hotel's
/// authorisation model in two places.
/// </para>
/// </remarks>
public interface IFeedAdminService
{
    /// <summary>
    /// Soft-deletes a post and its comments, whoever wrote them. False when there is no live post
    /// with that id — including one already taken down, so a retried request says so rather than
    /// reporting a second success.
    /// </summary>
    Task<bool> RemoveAsync(int postId, CancellationToken ct);
}
