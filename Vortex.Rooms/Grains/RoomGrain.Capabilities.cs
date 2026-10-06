using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Action;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Enums.Games;
using Vortex.Primitives.Rooms.Games;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Avatars;
using Vortex.Primitives.Rooms.Object.Furniture;

namespace Vortex.Rooms.Grains;

/// <summary>
/// The in-process capabilities the room offers to the object logic running inside its turn.
/// <para>
/// Every member is an explicit interface implementation. That is deliberate: it keeps these off
/// <c>RoomGrain</c>'s own surface, which is already large, and it lets two subsystems keep a method
/// of the same name without colliding.
/// </para>
/// <para>
/// None of these interfaces derives from a grain interface, so Orleans generates no proxy for them
/// and a call through one stays an ordinary method call on this activation.
/// </para>
/// </summary>
public sealed partial class RoomGrain : IRoomLookup, IRoomMapAccess, IRoomGameAccess
{
    private static readonly IReadOnlySet<RoomObjectId> EmptyStack = new HashSet<RoomObjectId>();

    IRoomItem? IRoomLookup.FindItem(RoomObjectId objectId) =>
        _state.ItemsById.TryGetValue(objectId, out IRoomItem? item) ? item : null;

    IRoomAvatar? IRoomLookup.FindAvatar(RoomObjectId objectId) =>
        _state.AvatarsByObjectId.TryGetValue(objectId, out IRoomAvatar? avatar) ? avatar : null;

    IRoomAvatar? IRoomLookup.FindAvatarByPlayer(PlayerId playerId) =>
        _state.AvatarsByPlayerId.TryGetValue(playerId, out RoomObjectId objectId)
        && _state.AvatarsByObjectId.TryGetValue(objectId, out IRoomAvatar? avatar)
            ? avatar
            : null;

    bool IRoomLookup.TryFindItem(RoomObjectId objectId, [NotNullWhen(true)] out IRoomItem? item) =>
        _state.ItemsById.TryGetValue(objectId, out item);

    bool IRoomLookup.TryFindAvatar(
        RoomObjectId objectId,
        [NotNullWhen(true)] out IRoomAvatar? avatar
    ) => _state.AvatarsByObjectId.TryGetValue(objectId, out avatar);

    bool IRoomLookup.TryFindAvatarByPlayer(
        PlayerId playerId,
        [NotNullWhen(true)] out IRoomAvatar? avatar
    )
    {
        if (_state.AvatarsByPlayerId.TryGetValue(playerId, out RoomObjectId objectId))
        {
            return _state.AvatarsByObjectId.TryGetValue(objectId, out avatar);
        }

        avatar = null;

        return false;
    }

    IReadOnlyCollection<IRoomItem> IRoomLookup.Items => _state.ItemsById.Values;

    IReadOnlyCollection<IRoomAvatar> IRoomLookup.Avatars => _state.AvatarsByObjectId.Values;

    int IRoomLookup.AvatarCount => _state.AvatarsByPlayerId.Count;

    int IRoomMapAccess.Width => MapModule.Width;

    int IRoomMapAccess.Height => MapModule.Height;

    int IRoomMapAccess.TileCount => _state.TileFloorStacks.Length;

    int IRoomMapAccess.ToIdx(int x, int y) => MapModule.ToIdx(x, y);

    (int x, int y) IRoomMapAccess.GetTileXY(int idx) => MapModule.GetTileXY(idx);

    int IRoomMapAccess.GetDistanceBetween(int a, int b) => MapModule.GetDistanceBetween(a, b);

    (int dx, int dy) IRoomMapAccess.GetDirectionOffset(Rotation dir) =>
        MapModule.GetDirectionOffset(dir);

    bool IRoomMapAccess.TryGetTileInFront(int index, Rotation direction, out int nextIndex) =>
        MapModule.TryGetTileInFront(index, direction, out nextIndex);

    bool IRoomMapAccess.CanAvatarWalk(
        IRoomAvatar avatar,
        int tileIdx,
        bool isGoal,
        bool isDiagonalCheck
    ) => MapModule.CanAvatarWalk(avatar, tileIdx, isGoal, isDiagonalCheck);

    RoomTileFlags IRoomMapAccess.TileFlagsAt(int tileIndex) =>
        tileIndex >= 0 && tileIndex < _state.TileFlags.Length
            ? _state.TileFlags[tileIndex]
            : RoomTileFlags.None;

    IReadOnlySet<RoomObjectId> IRoomMapAccess.FloorStackAt(int tileIndex) =>
        tileIndex >= 0 && tileIndex < _state.TileFloorStacks.Length
            ? _state.TileFloorStacks[tileIndex]
            : EmptyStack;

    IReadOnlySet<RoomObjectId> IRoomMapAccess.AvatarStackAt(int tileIndex) =>
        tileIndex >= 0 && tileIndex < _state.TileAvatarStacks.Length
            ? _state.TileAvatarStacks[tileIndex]
            : EmptyStack;

    GameTeamColor IRoomGameAccess.GetTeam(PlayerId playerId) => GameRuntime.GetTeam(playerId);

    int IRoomGameAccess.GetTeamScore(GameTeamColor team) => GameRuntime.GetTeamScore(team);

    IReadOnlyList<PlayerId> IRoomGameAccess.GetPlayersInTeam(GameTeamColor team) =>
        GameRuntime.GetPlayersInTeam(team);

    Task<bool> IRoomGameAccess.StartGameAsync(
        RoomObjectId source,
        GameId game,
        CancellationToken ct
    ) => GameRuntime.StartGameAsync(source, game, ct);

    Task<bool> IRoomGameAccess.EndGameAsync(
        RoomObjectId source,
        GameId game,
        CancellationToken ct
    ) => GameRuntime.EndGameAsync(source, game, ct);

    Task IRoomGameAccess.JoinTeamAsync(
        PlayerId playerId,
        GameTeamColor team,
        CancellationToken ct
    ) => GameRuntime.JoinTeamAsync(playerId, team, ct);

    Task IRoomGameAccess.LeaveTeamAsync(PlayerId playerId, CancellationToken ct) =>
        GameRuntime.LeaveTeamAsync(playerId, ct);

    Task<bool> IRoomGameAccess.TryGiveScoreToTeamAsync(
        RoomObjectId box,
        GameTeamColor team,
        int amount,
        int cap,
        CancellationToken ct
    ) => GameRuntime.TryGiveScoreToTeamAsync(box, team, amount, cap, ct);

    Task<bool> IRoomGameAccess.TryGiveScoreToPlayerTeamAsync(
        RoomObjectId box,
        PlayerId playerId,
        int amount,
        int cap,
        CancellationToken ct
    ) => GameRuntime.TryGiveScoreToPlayerTeamAsync(box, playerId, amount, cap, ct);

    bool IRoomGameAccess.IsRunning(GameId game) => GameRuntime.IsRunningGame(game);

    Task IRoomGameAccess.SignalAsync(GameSignal signal, CancellationToken ct) =>
        GameRuntime.SignalAsync(signal, ct);

    void IRoomGameAccess.LockMovement(PlayerId playerId) =>
        GameRuntime.Chrome.LockMovement(playerId);

    void IRoomGameAccess.UnlockMovement(PlayerId playerId) =>
        GameRuntime.Chrome.UnlockMovement(playerId);
}
