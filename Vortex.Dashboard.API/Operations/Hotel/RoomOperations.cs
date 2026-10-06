using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;
using Vortex.Dashboard.API.Operations.Hotel.Contracts;
using Vortex.Observability.Diagnostics;
using Vortex.Primitives.Action;
using Vortex.Primitives.Catalog.Snapshots;
using Vortex.Primitives.Moderation;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Enums.Wallet;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Snapshots.Avatars;

namespace Vortex.Dashboard.API.Operations.Hotel;

internal sealed class RoomOperations(
    OperationRunner runner,
    IGrainFactory grainFactory,
    IVortexMetrics metrics,
    StaffActorAccount staffActor
)
{
    private readonly OperationRunner _runner = runner;
    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly IVortexMetrics _metrics = metrics;
    private readonly StaffActorAccount _staffActor = staffActor;

    public async Task<ImmutableArray<RoomSummaryDto>> GetActiveRoomsAsync()
    {
        ImmutableArray<RoomSummarySnapshot> rooms;

        using (_metrics.MeasureRoomDirectoryCall(nameof(IRoomDirectoryGrain.GetActiveRoomsAsync)))
        {
            rooms = await _grainFactory
                .GetRoomDirectoryGrain()
                .GetActiveRoomsAsync()
                .ConfigureAwait(false);
        }

        return
        [
            .. rooms.Select(r => new RoomSummaryDto(
                r.RoomId.Value,
                r.Name,
                r.OwnerId.Value,
                r.OwnerName,
                r.Population,
                r.LastUpdatedUtc,
                r.RaidIncidentActive
            )),
        ];
    }

    public async Task<ImmutableArray<RoomOccupantSnapshot>> GetRoomOccupantsAsync(
        int roomId,
        CancellationToken ct
    )
    {
        ImmutableArray<RoomAvatarSnapshot> avatars = await _grainFactory
            .GetRoomAvatars(new RoomId(roomId))
            .GetAllAvatarSnapshotsAsync(ct)
            .ConfigureAwait(false);

        return
        [
            .. avatars
                .OfType<RoomPlayerAvatarSnapshot>()
                .Select(a => new RoomOccupantSnapshot { PlayerId = a.WebId, Name = a.Name }),
        ];
    }

    public Task<OperationResult> ForceCloseRoomAsync(
        ForceCloseRoomRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.room.close",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: request.RoomId,
            detail: new { },
            work: _ => _grainFactory.GetRoomCore(new RoomId(request.RoomId)).DeactivateRoomAsync(),
            ct,
            AuditCategory.Moderation
        );

    public Task<OperationResult> KickFromRoomAsync(
        KickFromRoomRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.room.kick",
            actor,
            request.Reason,
            targetPlayerId: request.PlayerId,
            roomId: request.RoomId,
            detail: new { },
            work: async c =>
            {
                PlayerId staffActor = await _staffActor.PlayerIdAsync(c).ConfigureAwait(false);
                RoomId roomId = new(request.RoomId);
                ActionContext actorCtx = ActionContext.CreateForPlayer(staffActor, roomId);

                bool ok = await _grainFactory
                    .GetRoomModeration(roomId)
                    .KickUserAsync(actorCtx, new PlayerId(request.PlayerId), c)
                    .ConfigureAwait(false);

                if (!ok)
                {
                    throw new InvalidOperationException("kick_rejected");
                }
            },
            ct,
            AuditCategory.Moderation
        );

    /// <summary>
    /// Sends a placed item back to its owner's hand.
    /// </summary>
    /// <remarks>
    /// Through the room grain, never around it. A placed item is the room's live state: deleting or
    /// reassigning its row while the room holds it leaves everyone standing there looking at
    /// something that no longer exists. Going through the grain is also what makes the item vanish
    /// from their screens, because the room is what sends that packet.
    /// <para>
    /// This is what turns the item-revoke's <c>item_is_placed</c> refusal from a dead end into a
    /// first step: pick it up, then it is an ordinary item in a hand.
    /// </para>
    /// </remarks>
    public Task<OperationResult> PickUpFurnitureAsync(
        PickUpFurnitureRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.item.pickup",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: request.RoomId,
            detail: new { request.ItemId },
            work: async c =>
            {
                PlayerId staffActor = await _staffActor.PlayerIdAsync(c).ConfigureAwait(false);
                RoomId roomId = new(request.RoomId);
                ActionContext actorCtx = ActionContext.CreateForPlayer(staffActor, roomId);

                bool ok = await _grainFactory
                    .GetRoomFurni(roomId)
                    .RemoveItemByIdAsync(actorCtx, new RoomObjectId(request.ItemId), c)
                    .ConfigureAwait(false);

                if (!ok)
                {
                    // The room refused: the id is not in it, or it is not something that can be
                    // picked up. Either way the row is untouched, which is the point.
                    throw new InvalidOperationException("pickup_rejected");
                }
            },
            ct
        );

    /// <summary>
    /// Resolves (and caches for the process lifetime) the reserved dashboard staff actor's player
    /// id by name via the player directory — see <see cref="StaffActorName"/>. Throws if the
    /// <c>SeedDashboardStaffActor</c> migration has not run.
    /// </summary>
}

/// <summary>
/// JSON-safe projection of <see cref="RoomSummarySnapshot"/> for the dashboard. RoomId/OwnerId are
/// the strongly-typed <c>RoomId</c>/<c>PlayerId</c> record structs there, which System.Text.Json
/// serializes as <c>{"value": n}</c> objects rather than plain numbers when returned directly --
/// unwrapping to <see cref="int"/> here matches how every other dashboard read (audit, directory,
/// moderation) already unwraps these ids via <c>.Value</c> before returning them as JSON.
/// </summary>
public sealed record RoomSummaryDto(
    int RoomId,
    string Name,
    int OwnerId,
    string OwnerName,
    int Population,
    DateTime LastUpdatedUtc,
    /// <summary>The room is handling a raid at this moment — live state, gone when it passes.</summary>
    bool RaidIncidentActive
);
