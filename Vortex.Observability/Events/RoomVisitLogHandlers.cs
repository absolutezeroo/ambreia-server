using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Context;
using Vortex.Database.Entities.Room;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;

namespace Vortex.Observability.Events;

/// <summary>
/// Persists the structured room-visit history: who entered which room and when they left (a real
/// append-only log, not something to reconstruct by parsing the generic <see cref="Vortex.Primitives.Observability.AuditEvent"/>
/// JSON blob that <see cref="SessionLifecycleAuditHandlers"/> also writes from these same events).
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
            new RoomEntryLogEntity { RoomEntityId = e.RoomId, PlayerEntityId = e.PlayerId }
        );

        await dbCtx.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}

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
                && v.RoomEntityId == e.RoomId
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
