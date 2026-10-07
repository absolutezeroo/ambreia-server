using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Games;
using Vortex.Primitives.Rooms.Games.Components;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Avatars;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Protocol.Messages.Outgoing.Room.Engine;
using Vortex.Rooms.Games.Abstractions;
using Vortex.Rooms.Games.Arena;
using Vortex.Rooms.Games.Events;
using Vortex.Rooms.Games.Presentation;
using Vortex.Rooms.Games.Scoring;
using Vortex.Rooms.Games.Teams;
using Vortex.Rooms.Grains;

namespace Vortex.Rooms.Games.Runtime;

/// <summary>
/// The real <see cref="IRoomGameContext"/>: one per ARENA, translating the narrow vocabulary game
/// modules speak into the room grain's own APIs. It is the only file in the games tree that
/// knows what a <c>RoomGrain</c> is, which is what keeps every module testable against a fake.
/// <para>
/// The room's coordinates, pathfinding, occupancy and collision are used, never reimplemented: a
/// teleport is the map module's own <c>RollAvatar</c>, a ball hop is the same <c>RollFloorItem</c>
/// plus the client's slide bundle.
/// </para>
/// </summary>
internal sealed class RoomGameContext(
    RoomGrain roomGrain,
    RoomGameRuntime runtime,
    ArenaHost host,
    IGameArena arena
) : IRoomGameContext
{
    private readonly RoomGrain _roomGrain = roomGrain;
    private readonly RoomGameRuntime _runtime = runtime;
    private readonly ArenaHost _host = host;

    public RoomId RoomId => _roomGrain.RoomId;

    public ILogger Logger => _roomGrain._logger;

    public ArenaId ArenaId => _host.Id;

    /// <summary>This arena's ledger: the room's shared one when the game plays with the room's teams,
    /// a private one when it defines its own.</summary>
    public TeamBook Teams => _host.Teams;

    public TeamSet TeamSet => _host.Game.Profile.Teams;

    public HabboTeamPalette Palette => _host.Palette;

    public IGameChrome Chrome => _runtime.Chrome;

    public IGameArena Arena { get; } = arena;

    public IGameRandom Random => _host.Random;

    public GamePhase Phase => _host.Phase;

    public MatchId Match => _host.Match?.Id ?? MatchId.None;

    public long NowMs => _runtime.NowMs;

    public void KeepTicking() => _host.WantsIdleTick = true;

    public int MapWidth => _roomGrain.MapModule.Width;

    public bool InBounds(int x, int y) => _roomGrain.MapModule.InBounds(x, y);

    public bool InBounds(int tileIdx) => _roomGrain.MapModule.InBounds(tileIdx);

    public int ToIdx(int x, int y) => _roomGrain.MapModule.ToIdx(x, y);

    public (int X, int Y) ToXY(int tileIdx) => _roomGrain.MapModule.GetTileXY(tileIdx);

    public Altitude TileHeight(int tileIdx) =>
        _roomGrain.MapModule.InBounds(tileIdx)
            ? _roomGrain._state.TileHeights[tileIdx]
            : Altitude.Zero;

    public bool IsTileOpenForItem(int tileIdx)
    {
        if (!_roomGrain.MapModule.InBounds(tileIdx))
        {
            return false;
        }

        return !_roomGrain
            ._state.TileFlags[tileIdx]
            .Has(RoomTileFlags.Disabled, RoomTileFlags.StackBlocked);
    }

    public bool HasAvatarOn(int tileIdx) =>
        _roomGrain.MapModule.InBounds(tileIdx)
        && _roomGrain._state.TileAvatarStacks[tileIdx].Count > 0;

    public void RecomputeTile(int x, int y) => _roomGrain.MapModule.ComputeTile(x, y);

    public IReadOnlyList<PlayerId> PlayersOn(int tileIdx)
    {
        List<PlayerId> players = [];

        if (!_roomGrain.MapModule.InBounds(tileIdx))
        {
            return players;
        }

        foreach (RoomObjectId avatarId in _roomGrain._state.TileAvatarStacks[tileIdx])
        {
            if (
                _roomGrain._state.AvatarsByObjectId.TryGetValue(avatarId, out IRoomAvatar? avatar)
                && avatar is IRoomPlayer player
            )
            {
                players.Add(player.PlayerId);
            }
        }

        return players;
    }

    public bool TryGetPlayerTile(PlayerId playerId, out int tileIdx)
    {
        if (TryGetAvatar(playerId, out IRoomAvatar? avatar) && avatar is not null)
        {
            tileIdx = _roomGrain.MapModule.ToIdx(avatar.X, avatar.Y);

            return true;
        }

        tileIdx = -1;

        return false;
    }

    public bool TryGetPlayerPosition(PlayerId playerId, out int x, out int y)
    {
        if (TryGetAvatar(playerId, out IRoomAvatar? avatar) && avatar is not null)
        {
            x = avatar.X;
            y = avatar.Y;

            return true;
        }

        x = 0;
        y = 0;

        return false;
    }

    public bool TryGetPlayerFacing(PlayerId playerId, out Rotation facing)
    {
        if (TryGetAvatar(playerId, out IRoomAvatar? avatar) && avatar is not null)
        {
            facing = avatar.Rotation;

            return true;
        }

        facing = Rotation.None;

        return false;
    }

    public bool TryGetPlayerGoalTile(PlayerId playerId, out int tileIdx)
    {
        if (TryGetAvatar(playerId, out IRoomAvatar? avatar) && avatar is not null)
        {
            tileIdx = avatar.GoalTileId;

            return tileIdx >= 0;
        }

        tileIdx = -1;

        return false;
    }

    public bool TryGetTileInFront(int tileIdx, Rotation direction, out int nextTileIdx) =>
        _roomGrain.MapModule.TryGetTileInFront(tileIdx, direction, out nextTileIdx);

    public string? NameOf(PlayerId playerId) =>
        TryGetAvatar(playerId, out IRoomAvatar? avatar) ? avatar?.Name : null;

    public void CancelWalk(PlayerId playerId)
    {
        if (TryGetAvatar(playerId, out IRoomAvatar? avatar) && avatar is not null)
        {
            _roomGrain.AvatarModule.CancelWalk(avatar);
        }
    }

    public async Task MovePlayerAsync(PlayerId playerId, int tileIdx)
    {
        if (
            !_roomGrain.MapModule.InBounds(tileIdx)
            || !TryGetAvatar(playerId, out IRoomAvatar? avatar)
            || avatar is null
        )
        {
            return;
        }

        // Before the move, never after: the walk tick steps through TilePath, and that path was
        // routed from the tile the avatar is being taken off. Left in place it walks them straight
        // back towards the old goal with the move status still on — which reads as the teleport
        // being undone, with the avatar stuck in its walk animation. The wired move-user path has
        // always cancelled here; this one, which every game teleport goes through (the Banzai random
        // teleporter, the Freeze exit), did not.
        _roomGrain.AvatarModule.CancelWalk(avatar);

        _roomGrain.MapModule.RollAvatar(avatar, tileIdx, _roomGrain._state.TileHeights[tileIdx]);

        await _roomGrain.SendComposerToRoomAsync(
            new UserUpdateMessageComposer { Avatars = [avatar.GetSnapshot()] }
        );
    }

    public async Task FacePlayerAsync(PlayerId playerId, int targetX, int targetY)
    {
        if (!TryGetAvatar(playerId, out IRoomAvatar? avatar) || avatar is null)
        {
            return;
        }

        if (avatar.X == targetX && avatar.Y == targetY)
        {
            return;
        }

        Rotation facing = RotationExtensions.FromPoints(avatar.X, avatar.Y, targetX, targetY);
        avatar.SetHeadRotation(facing);
        avatar.SetBodyRotation(facing);

        await _roomGrain.SendComposerToRoomAsync(
            new UserUpdateMessageComposer { Avatars = [avatar.GetSnapshot()] }
        );
    }

    public async Task SlideItemAsync(IGameComponent component, int toTileIdx)
    {
        if (
            !_roomGrain.MapModule.InBounds(toTileIdx)
            || component.Context.RoomObject is not IRoomFloorItem item
        )
        {
            return;
        }

        int fromIdx = _roomGrain.MapModule.ToIdx(item.X, item.Y);

        if (fromIdx == toTileIdx)
        {
            return;
        }

        (int fromX, int fromY) = _roomGrain.MapModule.GetTileXY(fromIdx);
        (int toX, int toY) = _roomGrain.MapModule.GetTileXY(toTileIdx);

        Altitude fromZ = item.Z;
        Altitude toZ = _roomGrain._state.TileHeights[toTileIdx];

        // Authoritative position first, animation second: a client that never receives the slide
        // still sees the ball where the server says it is on its next full room update.
        _roomGrain.MapModule.RollFloorItem(item, toTileIdx, toZ);

        await _roomGrain.SendComposerToRoomAsync(
            new SlideObjectBundleMessageComposer
            {
                FromX = fromX,
                FromY = fromY,
                ToX = toX,
                ToY = toY,
                FloorItemHeights = [(item.ObjectId, fromZ, toZ)],
                Avatar = null,
            }
        );
    }

    public Task ScoreAsync(GameScore score, CancellationToken ct) =>
        _runtime.ApplyScoreAsync(_host, score, ct);

    /// <summary>Stamps the event with this game and its live match before fanning it out, so a
    /// module never has to name the match it is in — and cannot name the wrong one.</summary>
    public Task PublishAsync(GameEvent evt, CancellationToken ct) =>
        _runtime.PublishGameEventAsync(evt with { Game = _host.Id.Game, Match = Match }, ct);

    public Task RequestMatchEndAsync(CancellationToken ct) =>
        _runtime.RequestRoundEndAsync(_host, ct);

    public Task<ImmutableDictionary<string, string>> GetConfigAsync(ImmutableArray<string> keys) =>
        _roomGrain._grainFactory.GetServerConfigGrain().GetManyAsync(keys);

    private bool TryGetAvatar(PlayerId playerId, out IRoomAvatar? avatar)
    {
        avatar = null;

        return _roomGrain._state.AvatarsByPlayerId.TryGetValue(playerId, out RoomObjectId objectId)
            && _roomGrain._state.AvatarsByObjectId.TryGetValue(objectId, out avatar);
    }
}
