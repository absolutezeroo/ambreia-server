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
using Vortex.Rooms.Grains.Zones.Runtime;
using Vortex.Rooms.Grains.Zones.Triggers;

namespace Vortex.Rooms.Grains.Zones.Modules;

/// <summary>
/// The zone's triggers (plan D11–D15), listening on the room's event bus: an area entered or left,
/// a furni used, or the clock. A trigger fires when its target matches, it is active, its cooldown
/// is over (per player, or global for the clock) and every condition holds; its actions run in
/// order. A chain of triggers setting each other off stops at <c>RoomConfig.ZoneTriggerMaxDepth</c>
/// deep or after <c>RoomConfig.ZoneTriggerChainBudget</c> runs, whichever comes first.
/// </summary>
public sealed class ZoneTriggerModule(RoomGrain roomGrain) : IRoomEventListener
{
    private int _depth;

    /// <summary>Trigger runs in the chain under way: set off by one event, reset by the next.</summary>
    private int _runs;

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
        // Twenty times a second per room: a zone without a clock stops at the first check.
        if (roomGrain._state.ZoneRuntime is not { Compiled.Timers.Count: > 0 } zone)
        {
            return;
        }

        long now = Time.GetUtcNow().ToUnixTimeMilliseconds();

        foreach ((ZoneTrigger timer, int seconds) in zone.Compiled.Timers)
        {
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
            && roomGrain._zoneRules.AllHold(conditions, ContextFor(zone, playerId));

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

        // Most steps and uses match no trigger: the lookup answers without allocating.
        IEnumerable<ZoneTrigger> matching = zone.Compiled.TriggersFor(type, target);

        return !matching.Any()
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
        // An event from outside any trigger starts a new chain, with a fresh budget.
        if (_depth == 0)
        {
            _runs = 0;
        }

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
        // Plan D15: depth alone lets N triggers per area run N^depth times; the chain set off by
        // one event also has a budget of runs.
        if (++_runs > roomGrain._roomConfig.ZoneTriggerChainBudget)
        {
            if (_runs == roomGrain._roomConfig.ZoneTriggerChainBudget + 1)
            {
                roomGrain._logger.LogWarning(
                    "Zone triggers in room {RoomId} ran {Runs} times for one event; the chain stops here",
                    roomGrain.RoomId.Value,
                    roomGrain._roomConfig.ZoneTriggerChainBudget
                );
            }

            return;
        }

        // Per player; what nobody set off (a clock) cools down for everyone, under player 0.
        string cooldown = $"trigger:{trigger.Id}";
        long now = roomGrain.NowMs();

        if (zone.Cooldowns.IsCooling(playerId, cooldown, now))
        {
            return;
        }

        ZoneTriggerContext ctx = ContextFor(zone, playerId);

        if (!roomGrain._zoneRules.AllHold(trigger.Conditions, ctx))
        {
            return;
        }

        if (trigger.CooldownMs > 0)
        {
            zone.Cooldowns.Start(playerId, cooldown, now + trigger.CooldownMs, now);
        }

        foreach (ZoneAction action in trigger.Actions)
        {
            try
            {
                await roomGrain._zoneRules.RunAsync(action, ctx, ct);
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
        new(
            roomGrain,
            zone,
            playerId,
            PlayerAvatar(playerId),
            Time.GetLocalNow(),
            zone.Facts.GetValueOrDefault(playerId)
        );

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
