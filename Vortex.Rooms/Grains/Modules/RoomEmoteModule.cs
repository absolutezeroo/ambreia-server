using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Action;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Events;
using Vortex.Primitives.Rooms.Events.Player;
using Vortex.Primitives.Rooms.Object;
using Vortex.Protocol.Messages.Outgoing.Rpg.Emote;

namespace Vortex.Rooms.Grains.Modules;

/// <summary>
/// RP actions of an avatar (wave, laugh, dance…): a configured key of a player standing here is shown
/// to the whole room, at most one per <c>EmoteIntervalMs</c>. Nothing is kept but each player's last
/// action time, forgotten when the player leaves.
/// </summary>
public sealed class RoomEmoteModule(RoomGrain roomGrain) : IRoomEventListener
{
    private readonly HashSet<string> _keys = new(
        roomGrain._roomConfig.EmoteKeys,
        StringComparer.Ordinal
    );
    private readonly Dictionary<PlayerId, long> _lastAtMs = [];

    public async Task EmoteAsync(ActionContext ctx, string key)
    {
        if (
            !_keys.Contains(key)
            || !roomGrain._state.AvatarsByPlayerId.TryGetValue(
                ctx.PlayerId,
                out RoomObjectId objectId
            )
        )
        {
            return;
        }

        long now = roomGrain.NowMs();

        if (
            _lastAtMs.TryGetValue(ctx.PlayerId, out long last)
            && now - last < roomGrain._roomConfig.EmoteIntervalMs
        )
        {
            return;
        }

        _lastAtMs[ctx.PlayerId] = now;

        await roomGrain.SendComposerToRoomAsync(
            new RpgEmoteMessageComposer { ObjectId = objectId.Value, Key = key }
        );
    }

    public Task OnRoomEventAsync(RoomEvent evt, CancellationToken ct)
    {
        if (evt is PlayerLeftEvent left)
        {
            _lastAtMs.Remove(left.PlayerId);
        }

        return Task.CompletedTask;
    }
}
