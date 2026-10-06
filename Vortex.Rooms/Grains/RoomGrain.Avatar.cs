using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vortex.Primitives.Action;
using Vortex.Primitives.Events;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Orleans.Snapshots.Players;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Events.Player;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Avatars;
using Vortex.Primitives.Rooms.Snapshots.Avatars;
using Vortex.Protocol.Messages.Outgoing.Room.Action;

namespace Vortex.Rooms.Grains;

public sealed partial class RoomGrain
{
    public async Task<bool> CreateAvatarFromPlayerAsync(
        ActionContext ctx,
        PlayerSummarySnapshot snapshot,
        CancellationToken ct
    )
    {
        try
        {
            IRoomAvatar avatar = await AvatarModule.CreateAvatarFromPlayerAsync(ctx, snapshot, ct);

            // Restore the effect the player was wearing (persisted selection) so it shows on entry and to
            // everyone already in the room. Late joiners re-sync the same field via the room-entry handler.
            int wornEffect = await _grainFactory
                .GetPlayerEffectGrain(snapshot.PlayerId)
                .GetSelectedEffectAsync(ct);

            if (wornEffect > 0 && avatar.SetEffect(wornEffect))
            {
                await SendComposerToRoomAsync(
                    new AvatarEffectMessageComposer
                    {
                        UserId = avatar.ObjectId,
                        EffectId = wornEffect,
                        DelayMilliseconds = 0,
                    }
                );
            }

            await PublishRoomEventAsync(
                new PlayerEnterEvent
                {
                    RoomId = _state.RoomId,
                    CausedBy = ctx,
                    PlayerId = snapshot.PlayerId,
                },
                ct
            );

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                $"Failed to create avatar for player {snapshot.PlayerId} in room {_state.RoomId}."
            );

            return false;
        }
    }

    public async Task ClickCharacterAsync(
        ActionContext ctx,
        int targetObjectId,
        CancellationToken ct
    )
    {
        if (ctx.Origin != ActionOrigin.Player || ctx.PlayerId <= 0)
        {
            return;
        }

        // Only clicks on another player's avatar are announced — not bots or pets.
        if (
            !_state.AvatarsByObjectId.TryGetValue(targetObjectId, out IRoomAvatar? avatar)
            || avatar is not IRoomPlayer target
        )
        {
            return;
        }

        await PublishRoomEventAsync(
            new PlayerClickedPlayerEvent
            {
                RoomId = _state.RoomId,
                CausedBy = ctx,
                PlayerId = ctx.PlayerId,
                TargetPlayerId = target.PlayerId,
            },
            ct
        );
    }

    public async Task<bool> RemoveAvatarFromPlayerAsync(
        ActionContext ctx,
        PlayerId playerId,
        CancellationToken ct
    )
    {
        try
        {
            await CloseTradeForLeavingPlayerAsync(playerId, ct);
            await CancelMysteryBoxSessionsForLeavingPlayerAsync(playerId);

            await AvatarModule.RemoveAvatarFromPlayerAsync(ctx, playerId, ct);

            await PublishRoomEventAsync(
                new PlayerLeftEvent
                {
                    RoomId = _state.RoomId,
                    CausedBy = ctx,
                    PlayerId = playerId,
                },
                ct
            );

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                $"Failed to remove avatar for player {playerId} in room {_state.RoomId}."
            );

            return false;
        }
    }

    public async Task<bool> WalkAvatarToAsync(
        ActionContext ctx,
        int targetX,
        int targetY,
        int? targetZKey,
        CancellationToken ct
    )
    {
        try
        {
            return await AvatarModule.WalkAvatarToAsync(ctx, targetX, targetY, targetZKey, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                $"Failed to walk avatar for player {ctx.PlayerId} in room {_state.RoomId} to ({targetX}, {targetY})."
            );

            return false;
        }
    }

    public async Task<bool> UpdateAvatarWithPlayerAsync(
        PlayerSummarySnapshot snapshot,
        CancellationToken ct
    )
    {
        try
        {
            return await AvatarModule.UpdateAvatarWithPlayerAsync(snapshot, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                $"Failed to update avatar for player {snapshot.PlayerId} in room {_state.RoomId}"
            );

            return false;
        }
    }

    /// <summary>The client's user-action codes for the two actions that carry an index.</summary>
    private const int SignActionCode = 10;

    private const int DanceActionCode = 11;

    /// <summary>
    /// Announces an action a room listener can watch for. Published from here rather than from the
    /// handlers because a dance also arrives from a bot command, and a listener that heard only the
    /// ones that came in on a packet would be told a lie.
    /// </summary>
    private Task PublishPerformedActionAsync(
        ActionContext ctx,
        int actionCode,
        int extra,
        CancellationToken ct
    ) =>
        PublishRoomEventAsync(
            new PlayerPerformedActionEvent
            {
                RoomId = RoomId,
                CausedBy = ctx,
                PlayerId = ctx.PlayerId,
                ActionCode = actionCode,
                Extra = extra,
            },
            ct
        );

    /// <summary>
    /// The client's user-action code for one of our expressions, or <c>-1</c> for an expression its
    /// action catalogue has no name for — nothing can ask for those, so nothing is lost by staying
    /// quiet.
    /// </summary>
    private static int ActionCodeFor(AvatarExpressionType expression) =>
        expression switch
        {
            AvatarExpressionType.Wave => 0,
            AvatarExpressionType.Blow => 1,
            AvatarExpressionType.Laugh => 2,
            AvatarExpressionType.Respect => 3,
            AvatarExpressionType.Idle => 5,
            _ => -1,
        };

    public async Task<bool> SetAvatarDanceAsync(
        ActionContext ctx,
        AvatarDanceType danceType,
        CancellationToken ct
    )
    {
        try
        {
            if (
                !_state.AvatarsByPlayerId.TryGetValue(ctx.PlayerId, out RoomObjectId objectId)
                || !await AvatarModule.SetAvatarDanceAsync(objectId, danceType, ct)
            )
            {
                return false;
            }

            // Stopping is not dancing. Anything that rewards a dance would otherwise be satisfied
            // by pressing the button twice.
            if (danceType != AvatarDanceType.None)
            {
                await _events
                    .PublishAsync(
                        new PlayerGesturedEvent(
                            ctx.PlayerId,
                            _state.RoomId.Value,
                            "dance",
                            danceType.ToValue()
                        ),
                        ct
                    )
                    .ConfigureAwait(true);

                await PublishPerformedActionAsync(ctx, DanceActionCode, (int)danceType, ct)
                    .ConfigureAwait(true);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                $"Failed to dance:{danceType} avatar for player {ctx.PlayerId} in room {_state.RoomId}"
            );

            return false;
        }
    }

    public Task<bool> GiveCarryItemAsync(PlayerId playerId, int itemId, CancellationToken ct) =>
        Task.FromResult(HandItemModule.Give(playerId, itemId));

    public Task<bool> DropCarryItemAsync(ActionContext ctx, CancellationToken ct) =>
        Task.FromResult(HandItemModule.Drop(ctx.PlayerId));

    public Task<bool> PassCarryItemAsync(
        ActionContext ctx,
        PlayerId targetPlayerId,
        CancellationToken ct
    ) => Task.FromResult(HandItemModule.Pass(ctx.PlayerId, targetPlayerId));

    public Task<bool> PassCarryItemToPetAsync(ActionContext ctx, int petId, CancellationToken ct) =>
        PetSystem.ConsumeHandItemAsync(ctx, petId, ct);

    public async Task<bool> SetAvatarEffectAsync(
        ActionContext ctx,
        int effectId,
        CancellationToken ct
    )
    {
        try
        {
            if (
                !_state.AvatarsByPlayerId.TryGetValue(ctx.PlayerId, out RoomObjectId objectId)
                || !await AvatarModule.SetAvatarEffectAsync(objectId, effectId, ct)
            )
            {
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                $"Failed to set effect:{effectId} avatar for player {ctx.PlayerId} in room {_state.RoomId}"
            );

            return false;
        }
    }

    public async Task<bool> SetAvatarExpressionAsync(
        ActionContext ctx,
        AvatarExpressionType expressionType,
        CancellationToken ct
    )
    {
        try
        {
            if (
                !_state.AvatarsByPlayerId.TryGetValue(ctx.PlayerId, out RoomObjectId objectId)
                || !await AvatarModule.SetAvatarExpressionAsync(objectId, expressionType, ct)
            )
            {
                return false;
            }

            if (expressionType == AvatarExpressionType.Wave)
            {
                await _events
                    .PublishAsync(
                        new PlayerGesturedEvent(ctx.PlayerId, _state.RoomId.Value, "wave"),
                        ct
                    )
                    .ConfigureAwait(true);
            }

            int actionCode = ActionCodeFor(expressionType);

            if (actionCode >= 0)
            {
                await PublishPerformedActionAsync(ctx, actionCode, -1, ct).ConfigureAwait(true);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                $"Failed to set expression:{expressionType} avatar for player {ctx.PlayerId} in room {_state.RoomId}"
            );

            return false;
        }
    }

    public async Task RespectPlayerAsync(
        ActionContext ctx,
        int targetPlayerId,
        CancellationToken ct
    )
    {
        try
        {
            await AvatarModule.RespectPlayerAsync((int)ctx.PlayerId, targetPlayerId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to respect player {Target} from {Giver} in room {RoomId}",
                targetPlayerId,
                ctx.PlayerId,
                _state.RoomId
            );
        }
    }

    public async Task<bool> SetAvatarPostureAsync(
        ActionContext ctx,
        AvatarPostureType postureType,
        CancellationToken ct
    )
    {
        try
        {
            if (
                !_state.AvatarsByPlayerId.TryGetValue(ctx.PlayerId, out RoomObjectId objectId)
                || !await AvatarModule.SetAvatarPostureAsync(objectId, postureType, ct)
            )
            {
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                $"Failed to set posture:{postureType} for player {ctx.PlayerId} in room {_state.RoomId}."
            );

            return false;
        }
    }

    public async Task<bool> SetAvatarSignAsync(ActionContext ctx, int signId, CancellationToken ct)
    {
        try
        {
            if (
                !_state.AvatarsByPlayerId.TryGetValue(ctx.PlayerId, out RoomObjectId objectId)
                || !await AvatarModule.SetAvatarSignAsync(objectId, signId, ct)
            )
            {
                return false;
            }

            await _events
                .PublishAsync(
                    new PlayerGesturedEvent(
                        ctx.PlayerId,
                        _state.RoomId.Value,
                        "sign",
                        signId.ToString(CultureInfo.InvariantCulture)
                    ),
                    ct
                )
                .ConfigureAwait(true);

            await PublishPerformedActionAsync(ctx, SignActionCode, signId, ct).ConfigureAwait(true);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                $"Failed to set sign:{signId} for player {ctx.PlayerId} in room {_state.RoomId}."
            );

            return false;
        }
    }

    public async Task<bool> LookToAvatarAsync(
        ActionContext ctx,
        int targetX,
        int targetY,
        CancellationToken ct
    )
    {
        try
        {
            if (
                !_state.AvatarsByPlayerId.TryGetValue(ctx.PlayerId, out RoomObjectId objectId)
                || !await AvatarModule.LookToAvatarAsync(objectId, targetX, targetY, ct)
            )
            {
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                $"Failed to look-to ({targetX},{targetY}) for player {ctx.PlayerId} in room {_state.RoomId}."
            );

            return false;
        }
    }

    public async Task SetAvatarTypingAsync(ActionContext ctx, bool isTyping, CancellationToken ct)
    {
        try
        {
            if (!_state.AvatarsByPlayerId.TryGetValue(ctx.PlayerId, out RoomObjectId objectId))
            {
                return;
            }

            await AvatarModule.SetAvatarTypingAsync(objectId, isTyping, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                $"Failed to set typing:{isTyping} for player {ctx.PlayerId} in room {_state.RoomId}."
            );
        }
    }

    public Task SendChatFromPlayerAsync(
        PlayerId playerId,
        string text,
        AvatarGestureType gesture,
        int styleId,
        List<(string, string, bool)> links,
        int trackingId,
        PlayerId? targetPlayerId = null,
        RoomChatType chatType = RoomChatType.Chat
    ) =>
        ChatSystem.SendChatFromPlayerAsync(
            playerId,
            text,
            gesture,
            styleId,
            links,
            trackingId,
            targetPlayerId,
            chatType
        );

    public async Task<ImmutableArray<RoomAvatarSnapshot>> GetAllAvatarSnapshotsAsync(
        CancellationToken ct
    )
    {
        ImmutableArray<RoomAvatarSnapshot> avatars = await AvatarModule
            .GetAllAvatarSnapshotsAsync(ct)
            .ConfigureAwait(true);
        ImmutableArray<RoomAvatarSnapshot> pets = await PetSystem
            .GetPlacedPetAvatarSnapshotsAsync(ct)
            .ConfigureAwait(true);
        ImmutableArray<RoomAvatarSnapshot> bots = await BotSystem
            .GetPlacedBotAvatarSnapshotsAsync(ct)
            .ConfigureAwait(true);

        return [.. avatars, .. pets, .. bots];
    }
}
