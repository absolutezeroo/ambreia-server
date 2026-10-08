using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Action;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Enums;

namespace Vortex.Primitives.Players.Grains;

public partial interface IPlayerPresenceGrain
{
    public Task<RoomPointerSnapshot> GetActiveRoomAsync();

    /// <summary>
    /// The only way into a room: the dungeon copy redirect, the room's door
    /// (<c>IRoomCore.AdmitAsync</c>), then the entry burst and the avatar. Setting the room pointer
    /// is not on this interface on purpose: a caller that could set it would skip the door.
    /// </summary>
    public Task EnterRoomAsync(
        ActionContext ctx,
        RoomId roomId,
        string password,
        CancellationToken ct
    );

    /// <summary>The client closes the room: the avatar leaves, a pending entry is dropped.</summary>
    public Task LeaveRoomAsync(CancellationToken ct);

    public Task OnControllerLevelUpdatedAsync(
        RoomId roomId,
        RoomControllerType controllerType,
        CancellationToken ct
    );
}
