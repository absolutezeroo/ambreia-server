using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Enums;

namespace Vortex.Primitives.Players.Grains;

public partial interface IPlayerPresenceGrain
{
    public Task<RoomPointerSnapshot> GetActiveRoomAsync();
    public Task<RoomPendingSnapshot> GetPendingRoomAsync();
    public Task SetActiveRoomAsync(RoomId roomId, CancellationToken ct);
    public Task ClearActiveRoomAsync(CancellationToken ct);
    public Task SetPendingRoomAsync(RoomId roomId, bool approved);

    /// <summary>The room a session re-attached to during the grace period, its avatar still
    /// standing there (spec Personnage §10); null otherwise. Sign-in forwards there first.</summary>
    public Task<RoomId?> GetResumableRoomAsync();

    /// <summary>True once when <paramref name="roomId"/> is the resumable room: the entry then keeps
    /// the avatar instead of making a second one.</summary>
    public Task<bool> TakeResumeAsync(RoomId roomId);
    public Task OnControllerLevelUpdatedAsync(
        RoomId roomId,
        RoomControllerType controllerType,
        CancellationToken ct
    );
}
