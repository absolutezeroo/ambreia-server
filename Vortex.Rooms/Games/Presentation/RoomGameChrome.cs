using System.Threading.Tasks;
using Vortex.Logging.Extensions;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums.Games;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Avatars;
using Vortex.Protocol.Messages.Outgoing.Room.Action;
using Vortex.Protocol.Messages.Outgoing.Room.Session;
using Vortex.Rooms.Games.Runtime;
using Vortex.Rooms.Grains;
using Vortex.Rooms.Object.Logic.Furniture.Floor;

namespace Vortex.Rooms.Games.Presentation;

/// <summary>
/// The one copy of the client-facing plumbing every room game needs. Before it existed each game
/// carried its own; the next game would have made a third. <c>RoomAvatarModule.SetAvatarEffectAsync</c>
/// stays deliberately separate: it dedupes on the current effect, which the game paths must not.
/// <para>All calls run inside the room grain's single-threaded turn.</para>
/// </summary>
public sealed class RoomGameChrome(RoomGrain roomGrain) : IGameChrome
{
    private const int NoEffect = 0;

    private readonly RoomGrain _roomGrain = roomGrain;

    public Task BroadcastEffectAsync(PlayerId playerId, int effectId)
    {
        if (!_roomGrain._state.AvatarsByPlayerId.TryGetValue(playerId, out RoomObjectId objectId))
        {
            return Task.CompletedTask;
        }

        if (_roomGrain._state.AvatarsByObjectId.TryGetValue(objectId, out IRoomAvatar? avatar))
        {
            avatar.SetEffect(effectId);
        }

        return _roomGrain.SendComposerToRoomAsync(
            new AvatarEffectMessageComposer
            {
                UserId = objectId,
                EffectId = effectId,
                DelayMilliseconds = 0,
            }
        );
    }

    public Task BroadcastTeamAuraAsync(PlayerId playerId, GameAuraSet aura, GameTeamColor team) =>
        BroadcastEffectAsync(
            playerId,
            HabboTeamPalette.IsColour(team) ? (int)aura + (int)team : NoEffect
        );

    public Task ClearEffectAsync(PlayerId playerId) => BroadcastEffectAsync(playerId, NoEffect);

    public Task SetPlayingModeAsync(PlayerId playerId, bool isPlaying) =>
        _roomGrain
            ._grainFactory.GetPlayerPresenceGrain(playerId)
            .SendComposerAsync(new YouArePlayingGameMessageComposer { IsPlaying = isPlaying });

    public void SetPlayingModeAndForget(PlayerId playerId, bool isPlaying) =>
        SetPlayingModeAsync(playerId, isPlaying)
            .LogAndForget(
                _roomGrain._logger,
                "Failed to clear game mode for player {PlayerId} leaving room {RoomId}",
                playerId,
                _roomGrain.RoomId
            );

    public Task BroadcastPlayerValueAsync(PlayerId playerId, int value)
    {
        if (!_roomGrain._state.AvatarsByPlayerId.TryGetValue(playerId, out RoomObjectId objectId))
        {
            return Task.CompletedTask;
        }

        return _roomGrain.SendComposerToRoomAsync(
            new GamePlayerValueMessageComposer { UserId = objectId, Value = value }
        );
    }

    public void LockMovement(PlayerId playerId)
    {
        if (!TryGetAvatar(playerId, out IRoomAvatar? avatar) || avatar is null)
        {
            return;
        }

        avatar.SetMovementLocked(true);
        _roomGrain.AvatarModule.CancelWalk(avatar);
    }

    public void UnlockMovement(PlayerId playerId)
    {
        if (TryGetAvatar(playerId, out IRoomAvatar? avatar) && avatar is not null)
        {
            avatar.SetMovementLocked(false);
        }
    }

    public void ResetGameTimers()
    {
        foreach (
            FurnitureGameTimerLogic timer in _roomGrain._state.ItemIndex.LogicsOf<FurnitureGameTimerLogic>()
        )
        {
            ((IGameCounter)timer).ResetClock();
        }
    }

    private bool TryGetAvatar(PlayerId playerId, out IRoomAvatar? avatar)
    {
        avatar = null;

        return _roomGrain._state.AvatarsByPlayerId.TryGetValue(playerId, out RoomObjectId objectId)
            && _roomGrain._state.AvatarsByObjectId.TryGetValue(objectId, out avatar);
    }
}
