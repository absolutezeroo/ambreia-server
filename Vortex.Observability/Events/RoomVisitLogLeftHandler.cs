using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Context;
using Vortex.Database.Entities.Room;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.World;

namespace Vortex.Observability.Events;

/// <summary>Closes the visit <see cref="RoomVisitLogEnteredHandler"/> opened.</summary>
public sealed class RoomVisitLogLeftHandler(IDbContextFactory<VortexDbContext> dbCtxFactory)
    : IEventHandler<PlayerLeftRoomEvent>
{
    public async ValueTask HandleAsync(
        PlayerLeftRoomEvent e,
        EventContext ctx,
        CancellationToken ct
    )
    {
        await using VortexDbContext dbCtx = await dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        RoomEntryLogEntity? openVisit = await dbCtx
            .RoomEntryLogs.Where(v =>
                v.PlayerEntityId == e.PlayerId.Value
                && v.RoomEntityId == ZoneInstanceIds.TemplateOf(new RoomId(e.RoomId)).Value
                && v.ExitedAt == null
            )
            .OrderByDescending(v => v.CreatedAt)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (openVisit is null)
        {
            return;
        }

        openVisit.ExitedAt = e.LeftAtUtc;

        await dbCtx.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
