using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Action;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object;

namespace Vortex.Primitives.Rooms;

public partial interface IRoomService
{
    public Task OpenRoomForPlayerIdAsync(
        ActionContext ctx,
        PlayerId playerId,
        RoomId roomId,
        CancellationToken ct,
        string password = ""
    );
    public Task CloseRoomForPlayerAsync(PlayerId playerId, CancellationToken ct);
    public Task ClickTileAsync(
        ActionContext ctx,
        int targetX,
        int targetY,
        int? targetZKey,
        CancellationToken ct
    );
}
