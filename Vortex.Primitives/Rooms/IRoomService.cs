using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Action;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object;

namespace Vortex.Primitives.Rooms;

public partial interface IRoomService
{
    Task<(RoomId RoomId, string Name)> CreateRoomAsync(
        string name,
        string description,
        string modelName,
        int categoryId,
        int maxPlayers,
        RoomTradeModeType tradeType,
        PlayerId playerId,
        CancellationToken ct
    );

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
    public Task PickupItemInRoomAsync(
        ActionContext ctx,
        RoomObjectId itemId,
        CancellationToken ct,
        bool isConfirm = true
    );
    public Task UseItemInRoomAsync(
        ActionContext ctx,
        RoomObjectId itemId,
        CancellationToken ct,
        int param = -1
    );
    public Task ClickItemInRoomAsync(
        ActionContext ctx,
        RoomObjectId itemId,
        CancellationToken ct,
        int param = -1
    );

    /// <summary>Resolves a pending doorbell ring: <paramref name="actorCtx"/> is the answering
    /// owner/rights-holder's own room context, <paramref name="targetPlayerId"/> the ringer.
    /// On admit, completes the ringer's room entry; on deny, tears down their pending session.</summary>
    public Task AnswerDoorbellAsync(
        ActionContext actorCtx,
        PlayerId targetPlayerId,
        bool admit,
        CancellationToken ct
    );
}
