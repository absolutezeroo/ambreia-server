using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vortex.Primitives.Action;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Events;
using Vortex.Primitives.Rooms.Events.Player;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Avatars;
using Vortex.Primitives.Rpg.Characters;
using Vortex.Protocol.Messages.Outgoing.Rpg.Interaction;
using Vortex.Rooms.Grains.State;

namespace Vortex.Rooms.Grains.Modules;

/// <summary>
/// What each player in the room aims at (spec F3b §6). A target is an avatar of this room, oneself
/// included; anything else clears it. The answer goes to the player alone. A departure clears the
/// target of whoever aimed at the one leaving, and forgets the leaver's own. Runtime only.
/// </summary>
public sealed class RoomTargetModule(RoomGrain roomGrain) : IRoomEventListener
{
    public async Task SelectAsync(ActionContext ctx, int objectId, CancellationToken ct)
    {
        // Only a player standing here aims: a choice that arrives after its sender left (or before
        // its avatar exists) would leave a target nothing clears, and tell it about the room.
        if (!roomGrain._state.AvatarsByPlayerId.ContainsKey(ctx.PlayerId))
        {
            return;
        }

        if (
            objectId <= 0
            || !roomGrain._state.AvatarsByObjectId.TryGetValue(
                new RoomObjectId(objectId),
                out IRoomAvatar? avatar
            )
            || avatar is not IRoomPlayer target
        )
        {
            roomGrain._state.Targets.Remove(ctx.PlayerId);
            await SendAsync(ctx.PlayerId, RpgTargetMessageComposer.None);

            return;
        }

        roomGrain._state.Targets[ctx.PlayerId] = new RoomTarget(target.ObjectId, target.PlayerId);

        // The character grain never calls the room back: no cycle with this non-reentrant grain.
        CharacterSummary? summary = await SummaryOrNullAsync(target.PlayerId);

        await SendAsync(
            ctx.PlayerId,
            new RpgTargetMessageComposer
            {
                ObjectId = objectId,
                Kind = RpgTargetMessageComposer.KindPlayer,
                Name = target.Name,
                Level = summary?.Level ?? 0,
                ClassId = summary?.ClassId ?? string.Empty,
            }
        );
    }

    public async Task OnRoomEventAsync(RoomEvent evt, CancellationToken ct)
    {
        if (evt is not PlayerLeftEvent left)
        {
            return;
        }

        roomGrain._state.Targets.Remove(left.PlayerId);

        PlayerId[] aimers =
        [
            .. roomGrain
                ._state.Targets.Where(t => t.Value.PlayerId == left.PlayerId)
                .Select(t => t.Key),
        ];

        foreach (PlayerId aimer in aimers)
        {
            roomGrain._state.Targets.Remove(aimer);
        }

        // Different players' presences: told together, not one after the other.
        await Task.WhenAll(aimers.Select(a => SendAsync(a, RpgTargetMessageComposer.None)));
    }

    /// <summary>A target's level and class; one that does not answer is shown without them, the
    /// target still chosen.</summary>
    private async Task<CharacterSummary?> SummaryOrNullAsync(PlayerId player)
    {
        try
        {
            return await roomGrain._grainFactory.GetPlayerCharacterGrain(player).GetSummaryAsync();
        }
        catch (Exception ex)
        {
            roomGrain._logger.LogWarning(
                ex,
                "The character of target {PlayerId} did not answer in room {RoomId}.",
                player.Value,
                roomGrain.RoomId.Value
            );

            return null;
        }
    }

    private async Task SendAsync(PlayerId to, IComposer composer)
    {
        try
        {
            await roomGrain._grainFactory.GetPlayerPresenceGrain(to).SendComposerAsync(composer);
        }
        catch (Exception ex)
        {
            roomGrain._logger.LogWarning(
                ex,
                "Could not send the target to player {PlayerId} in room {RoomId}.",
                to.Value,
                roomGrain.RoomId.Value
            );
        }
    }
}
