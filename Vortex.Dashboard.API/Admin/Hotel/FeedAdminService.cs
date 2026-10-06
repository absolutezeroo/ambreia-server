using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Context;
using Vortex.Database.Entities.Feed;
using Vortex.Primitives.Feed;

namespace Vortex.Dashboard.API.Admin.Hotel;

/// <summary>
/// Taking a post down from the website's social feed, whoever wrote it.
/// </summary>
/// <remarks>
/// <para>
/// The player's own delete lives on the website's API, in <c>WebApiFeedService.DeleteAsync</c>;
/// this is the operator's half and it is deliberately a second, tiny implementation rather than a
/// call into that one. <c>Vortex.Dashboard.API</c> does not reference <c>Vortex.WebApi</c>, and a
/// new reference between those two projects is a bigger decision than four lines of EF — it would
/// pull the whole public API surface in behind the operator console.
/// </para>
/// <para>
/// No capability check in here, and there must not be one: this type is unreachable except through
/// a route carrying <c>Capabilities.Dashboard.OpsFeedModerate</c>, and a second check in a second
/// place is a second thing to get wrong.
/// </para>
/// </remarks>
public sealed class FeedAdminService(IDbContextFactory<VortexDbContext> dbCtxFactory)
    : IFeedAdminService
{
    private readonly IDbContextFactory<VortexDbContext> _db = dbCtxFactory;

    public async Task<bool> RemoveAsync(int postId, CancellationToken ct)
    {
        await using VortexDbContext db = await _db.CreateDbContextAsync(ct).ConfigureAwait(false);

        // The global soft-delete filter on every VortexEntity means an already-removed post is
        // simply not found here, so a second call answers false rather than removing it twice.
        WebFeedPostEntity? post = await db
            .WebFeedPosts.FirstOrDefaultAsync(p => p.Id == postId, ct)
            .ConfigureAwait(false);

        if (post is null)
        {
            return false;
        }

        DateTime now = DateTime.UtcNow;

        post.DeletedAt = now;

        // The comments go with it, exactly as they do when the author deletes their own: a comment
        // is unreadable out of the thread it answers, and the feed lists roots only — one left
        // behind would sit in nobody's feed and be reachable by nobody.
        List<WebFeedPostEntity> comments = await db
            .WebFeedPosts.Where(p => p.ParentEntityId == postId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (WebFeedPostEntity child in comments)
        {
            child.DeletedAt = now;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return true;
    }
}
