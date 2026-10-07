using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Events;
using Vortex.Primitives.Rooms.Events.Zone;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Avatars;
using Vortex.Primitives.World.Layers;
using Vortex.Primitives.World.Rules;
using Vortex.Rooms.Grains.Zones.Triggers.Actions;
using Vortex.Rooms.Grains.Zones.Triggers.Conditions;

namespace Vortex.Rooms.Grains.Zones.Triggers;

/// <summary>
/// The zone's triggers (plan D11–D15), listening on the room's event bus: an area entered or left,
/// a furni used, or the clock. A trigger fires when its target matches, it is active, its cooldown
/// is over (per player, or global for the clock) and every condition holds; its actions run in
/// order. A chain of triggers setting each other off stops at <c>RoomConfig.ZoneTriggerMaxDepth</c>.
/// </summary>
public sealed class ZoneTriggerModule(RoomGrain roomGrain) : IRoomEventListener
{
    private int _depth;

    /// <summary>Server time for TimeOfDay and the clock; a test sets its own.</summary>
    public TimeProvider Time { get; set; } = TimeProvider.System;

    public Task OnRoomEventAsync(RoomEvent evt, CancellationToken ct) =>
        evt switch
        {
            ZoneAreaEnteredEvent e => FireAsync(
                ZoneTriggerType.EnterZone,
                e.AreaId,
                e.PlayerId,
                ct
            ),
            ZoneAreaLeftEvent e => FireAsync(ZoneTriggerType.LeaveZone, e.AreaId, e.PlayerId, ct),
            ZoneDecorUsedEvent e => FireAsync(ZoneTriggerType.Interact, e.DecorId, e.PlayerId, ct),
            _ => Task.CompletedTask,
        };

    /// <summary>The clock: each Timer trigger fires once per period of Target seconds.</summary>
    public async Task TickAsync(CancellationToken ct)
    {
        if (roomGrain._state.ZoneRuntime is not { } zone)
        {
            return;
        }

        long now = Time.GetUtcNow().ToUnixTimeMilliseconds();

        foreach (ZoneTrigger timer in zone.Compiled.Triggers[ZoneTriggerType.Timer])
        {
            if (!timer.Active || !int.TryParse(timer.Target, out int seconds) || seconds <= 0)
            {
                continue;
            }

            if (!zone.TimerNextAtMs.TryGetValue(timer.Id, out long next))
            {
                zone.TimerNextAtMs[timer.Id] = now + seconds * 1000L;

                continue;
            }

            if (now < next)
            {
                continue;
            }

            zone.TimerNextAtMs[timer.Id] = now + seconds * 1000L;

            await GuardedAsync(() => RunAsync(timer, zone, default, ct));
        }
    }

    /// <summary>Whether every condition holds for this player (a passage's, a trigger's).</summary>
    public bool ConditionsHold(IReadOnlyList<ZoneCondition> conditions, PlayerId playerId) =>
        conditions.Count == 0
        || roomGrain._state.ZoneRuntime is { } zone
            && ZoneConditions.AllHold(conditions, ContextFor(zone, playerId));

    private Task FireAsync(
        ZoneTriggerType type,
        string target,
        PlayerId playerId,
        CancellationToken ct
    )
    {
        if (roomGrain._state.ZoneRuntime is not { } zone)
        {
            return Task.CompletedTask;
        }

        ZoneTrigger[] matching =
        [
            .. zone.Compiled.Triggers[type].Where(t => t.Active && t.Target == target),
        ];

        return matching.Length == 0
            ? Task.CompletedTask
            : GuardedAsync(async () =>
            {
                foreach (ZoneTrigger trigger in matching)
                {
                    await RunAsync(trigger, zone, playerId, ct);
                }
            });
    }

    private async Task GuardedAsync(Func<Task> run)
    {
        if (_depth >= roomGrain._roomConfig.ZoneTriggerMaxDepth)
        {
            roomGrain._logger.LogWarning(
                "Zone triggers in room {RoomId} set each other off {Depth} deep; the chain stops here",
                roomGrain.RoomId.Value,
                _depth
            );

            return;
        }

        _depth++;

        try
        {
            await run();
        }
        finally
        {
            _depth--;
        }
    }

    private async Task RunAsync(
        ZoneTrigger trigger,
        ZoneRuntimeState zone,
        PlayerId playerId,
        CancellationToken ct
    )
    {
        Dictionary<string, long> cooldowns =
            playerId.Value > 0 && zone.Visitors.TryGetValue(playerId, out ZoneVisitor? visitor)
                ? visitor.TriggerReadyAtMs
                : zone.TriggerReadyAtMs;
        long now = roomGrain.NowMs();

        if (cooldowns.GetValueOrDefault(trigger.Id) > now)
        {
            return;
        }

        ZoneTriggerContext ctx = ContextFor(zone, playerId);

        if (!ZoneConditions.AllHold(trigger.Conditions, ctx))
        {
            return;
        }

        if (trigger.CooldownMs > 0)
        {
            cooldowns[trigger.Id] = now + trigger.CooldownMs;
        }

        foreach (ZoneAction action in trigger.Actions)
        {
            try
            {
                await ZoneActions.RunAsync(action, ctx, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                roomGrain._logger.LogWarning(
                    ex,
                    "Zone trigger {Trigger} action {Action} failed in room {RoomId}",
                    trigger.Id,
                    action.GetType().Name,
                    roomGrain.RoomId.Value
                );
            }
        }
    }

    private ZoneTriggerContext ContextFor(ZoneRuntimeState zone, PlayerId playerId) =>
        new(roomGrain, zone, playerId, PlayerAvatar(playerId), Time.GetLocalNow());

    /// <remarks>
    /// The arrival step runs inside the avatar's attach, before the room maps the player to it:
    /// on that step only the object index knows the avatar, and an EnterZone trigger on the
    /// arrival area must still be able to move them.
    /// </remarks>
    private IRoomPlayer? PlayerAvatar(PlayerId playerId) =>
        playerId.Value <= 0 ? null
        : roomGrain._state.AvatarsByPlayerId.TryGetValue(playerId, out RoomObjectId objectId)
        && roomGrain._state.AvatarsByObjectId.TryGetValue(objectId, out IRoomAvatar? avatar)
            ? avatar as IRoomPlayer
        : roomGrain
            ._state.AvatarsByObjectId.Values.OfType<IRoomPlayer>()
            .FirstOrDefault(p => p.PlayerId == playerId);
}
