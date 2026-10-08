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

/// <summary>
/// Persists the structured room-visit history: who entered which room and when they left (a real
/// append-only log, not something to reconstruct by parsing the generic
/// <see cref="Vortex.Primitives.Observability.AuditEvent"/> JSON blob that
/// <see cref="PlayerEnteredRoomAuditHandler"/> also writes from these same events). The exit is
/// <see cref="RoomVisitLogLeftHandler"/>.
/// </summary>
public sealed class RoomVisitLogEnteredHandler(IDbContextFactory<VortexDbContext> dbCtxFactory)
    : IEventHandler<PlayerEnteredRoomEvent>
{
    public async ValueTask HandleAsync(
        PlayerEnteredRoomEvent e,
        EventContext ctx,
        CancellationToken ct
    )
    {
        await using VortexDbContext dbCtx = await dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        dbCtx.RoomEntryLogs.Add(
            new RoomEntryLogEntity
            {
                // A dungeon copy has no row: its visits are its template's.
                RoomEntityId = ZoneInstanceIds.TemplateOf(new RoomId(e.RoomId)).Value,
                PlayerEntityId = e.PlayerId,
            }
        );

        await dbCtx.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
