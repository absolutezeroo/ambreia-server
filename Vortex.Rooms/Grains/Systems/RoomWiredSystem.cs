using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vortex.Logging.Extensions;
using Vortex.Primitives.Action;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Enums.Wired;
using Vortex.Primitives.Rooms.Events;
using Vortex.Primitives.Rooms.Events.Player;
using Vortex.Primitives.Rooms.Events.RoomItem;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Furniture;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Primitives.Rooms.Wired;
using Vortex.Protocol.Messages.Outgoing.Room.Engine;
using Vortex.Rooms.Grains.Storage;
using Vortex.Rooms.Object.Logic.Furniture.Floor.Wired;
using Vortex.Rooms.Object.Logic.Furniture.Floor.Wired.Actions;
using Vortex.Rooms.Object.Logic.Furniture.Floor.Wired.Addons;
using Vortex.Rooms.Object.Logic.Furniture.Floor.Wired.Conditions;
using Vortex.Rooms.Object.Logic.Furniture.Floor.Wired.Selectors;
using Vortex.Rooms.Object.Logic.Furniture.Floor.Wired.Triggers;
using Vortex.Rooms.Object.Logic.Furniture.Floor.Wired.Variables;
using Vortex.Rooms.Wired;
using Vortex.Rooms.Wired.Engine;
using Vortex.Rooms.Wired.Logs;

namespace Vortex.Rooms.Grains.Systems;

public sealed partial class RoomWiredSystem : IRoomEventListener
{
    /// <summary>The room's own engine, over the grain that owns it.</summary>
    public RoomWiredSystem(RoomGrain roomGrain)
        : this(new RoomGrainWiredHost(roomGrain)) { }

    /// <summary>
    /// The engine over anything that can answer for a room.
    /// </summary>
    /// <remarks>
    /// This is the constructor the whole extraction was for. Every component below reads the room
    /// through the host, so a test can supply one that is not a grain — which is what the parity
    /// matrix needs, and what was impossible while the engine held a RoomGrain field.
    /// </remarks>
    internal RoomWiredSystem(IWiredRoomHost host)
    {
        _host = host;
        _triggers = new WiredTriggerIndex(_host.View, _host.Diagnostics);
        _stacks = new WiredStackResolver(_host.View, _host.Diagnostics);

        // Random.Shared would do, and did — but a pile drawing "two effects, avoiding the last
        // three firings" has behaviour worth pinning, and pinning it needs a sequence a test can
        // predict.
        _policy = new WiredExecutionPolicy(_host.Diagnostics, Random.Shared);
        _schedule = new WiredExecutionScheduler();

        // The depth is read through a lambda rather than captured: it comes from configuration, and
        // an operator changing it should not have to restart the room to be heard (RFW-101).
        _callChain = new WiredCallChainGuard(_host.Diagnostics, () => _host.View.MaxCallChainDepth);
    }

    private readonly Queue<RoomEvent> _eventQueue = new();

    // Everything the engine needs from the room, and nothing else. It used to reach into the grain's
    // fields, which meant the only way to run the pipeline was to build most of a room.
    private readonly IWiredRoomHost _host;

    private IWiredRoomView Room => _host.View;

    private IWiredDiagnostics Diagnostics => _host.Diagnostics;

    /// <summary>
    /// The chain being executed right now, or null when none is.
    /// </summary>
    /// <remarks>
    /// The context variables — <c>@selector_furni_count</c>, <c>@signal_user_count</c> and the rest
    /// of that tab — describe the run, not the room: how many furni the selectors picked, who the
    /// signal carried. They are read through <see cref="IWiredVariable.TryGetValue"/>, which is
    /// handed a key and nothing else, so without somewhere to look the whole tab could only ever
    /// answer zero, and did.
    /// <para>
    /// An ambient field rather than a parameter threaded through <c>IWiredVariable</c>: that
    /// interface is implemented by every variable in the hotel, and all but four of them have no
    /// use for a context. It is safe here because a grain activation is single-threaded — no two
    /// chains in this room run at once — and it is cleared in a finally, so a throwing action
    /// cannot leave a stale context standing for the next read.
    /// </para>
    /// </remarks>
    public IWiredContext? CurrentContext { get; private set; }

    /// <summary>
    /// The context variables of the chain running right now, or null when none is.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="CurrentContext"/> and taken from the pending chain rather than from
    /// the per-action context, because a context variable set by one effect has to still be there
    /// for the next one. Same lifetime rules as the ambient beside it: published for the duration of
    /// an action and cleared in a finally, so a throwing effect cannot leave one chain's values
    /// standing for the next.
    /// </remarks>
    private KeyValueStore? _currentContextVariables;

    // Which trigger boxes are in the room and what they listen for, and how a tile's pile is
    // resolved. Both read the room through the host, so both can be exercised without one.
    private readonly WiredTriggerIndex _triggers;
    private readonly WiredStackResolver _stacks;
    private readonly WiredExecutionPolicy _policy;
    private readonly WiredExecutionScheduler _schedule;
    private readonly WiredCallChainGuard _callChain;

    private bool _firstRun = true;

    // Boxes currently lit by FlashActivationStateAsync, mapped to the room-clock time their visual
    // state reverts to unlit. Re-flashing a box simply pushes its revert time back.
    private readonly Dictionary<RoomObjectId, long> _flashRevertAtMs = [];

    // Events rejected because the queue hit WiredMaxQueuedEvents, reported once per tick in the
    // room's wired log instead of spamming one entry per drop.
    private int _droppedEventCount;

    // Delayed chains refused because the pending set hit WiredMaxPendingExecutions, reported the
    // same way and for the same reason.
    private int _droppedExecutionCount;

    // The room-clock time of the tick currently being processed, so an executing action (e.g. the Timer
    // Reset effect) can re-anchor schedules to "now" without threading the value through every call.
    private long _currentTickMs;

    private int _tickMs => Room.WiredTickMs;

    public Task OnRoomEventAsync(RoomEvent evt, CancellationToken ct)
    {
        if (evt is null)
        {
            return Task.CompletedTask;
        }

        switch (evt)
        {
            case RoomWiredStackChangedEvent:
                // A wired box was attached, detached, moved or reconfigured. Membership of the trigger
                // registries may have changed, so flag them for rebuild on the next tick. Piles are
                // resolved live at fire time, so nothing else needs invalidating here.
                _triggers.MarkDirty();
                break;
            case WiredVariableBoxChangedEvent boxEvt:
                {
                    foreach (int boxId in boxEvt.BoxIds)
                    {
                        _dirtyVariableBoxIds.Add(boxId);
                    }
                }
                break;
            case WiredVariableChangedEvent variableEvt:
                // The Variable FX displays bound to this variable, and then on to the trigger that
                // was always listening for it. Both, not one or the other: a bar that updated
                // instead of firing the trigger would be a silent behaviour change for every room
                // that already uses one.
                PublishFxStatusAsync(variableEvt)
                    .LogAndForget(
                        Diagnostics.Logger,
                        "Failed to publish a Variable FX status update."
                    );

                EnqueueRoomEvent(evt);

                // And once more per reading derived from it. A reading owns no value, so the write
                // lands on the parent and this is the only announcement it will ever get.
                foreach (WiredVariableChangedEvent derivedEvt in DerivedChanges(variableEvt))
                {
                    PublishFxStatusAsync(derivedEvt)
                        .LogAndForget(
                            Diagnostics.Logger,
                            "Failed to publish a Variable FX status update."
                        );

                    EnqueueRoomEvent(derivedEvt);
                }

                break;
            case PlayerLeftEvent playerLeftEvt:
                _playerActiveStore.RemovePlayerStore(playerLeftEvt.PlayerId);
                EnqueueRoomEvent(evt);
                break;
            case RoomItemDetachedEvent detatchedEvt:
                _furnitureActiveStore.RemoveFurnitureStore(detatchedEvt.ObjectId);
                break;
            default:
                EnqueueRoomEvent(evt);
                break;
        }

        return Task.CompletedTask;
    }

    private void EnqueueRoomEvent(RoomEvent evt)
    {
        // With a clean index we know exactly which event types have a listening trigger; anything
        // else would only consume dequeue budget before being discarded, so reject it now. A dirty
        // index means membership is unknown until the next tick's rebuild — enqueue conservatively.
        if (!_triggers.IsDirty && !_triggers.Listens(evt.GetType()))
        {
            Diagnostics.EventOutcome(WiredEventOutcome.IGNORED);

            return;
        }

        // WiredMaxEventsPerTick bounds the tick's work; this bounds the queue's memory under a
        // sustained storm. Rejecting the incoming event (rather than evicting an older one) keeps
        // trigger ordering intact for what was already accepted.
        if (_eventQueue.Count >= Room.MaxQueuedEvents)
        {
            _droppedEventCount++;
            Diagnostics.ChainStopped(WiredStopReason.QUEUE_DROP);

            return;
        }

        _eventQueue.Enqueue(evt);
    }

    public async Task ProcessWiredAsync(long now, CancellationToken ct)
    {
        if (now < Room.NextWiredBoundaryMs)
        {
            return;
        }

        Room.NextWiredBoundaryMs = Room.AdvanceBoundaryPast(now, _tickMs);

        _currentTickMs = now;

        await ProcessFlashRevertsAsync(now, ct);

        if (_droppedEventCount > 0)
        {
            WriteWiredRoomLog(
                WiredLogLevel.Warning,
                WiredLogSource.System,
                $"Dropped {_droppedEventCount} room event(s): the wired event queue was full."
            );

            _droppedEventCount = 0;
        }

        if (_droppedExecutionCount > 0)
        {
            WriteWiredRoomLog(
                WiredLogLevel.Warning,
                WiredLogSource.System,
                $"Dropped {_droppedExecutionCount} delayed chain(s): the room already has "
                    + $"{Room.MaxPendingExecutions} waiting to run."
            );

            _droppedExecutionCount = 0;
        }

        if (_firstRun)
        {
            await ProcessInternalVariablesAsync(now, ct);

            _firstRun = false;
        }

        await ProcessVariableBoxesAsync(now, ct);

        await PollVariablesAsync(now, ct);

        if (_triggers.IsDirty)
        {
            Diagnostics.IndexRebuilt();

            await _triggers.RebuildAsync(ct);
        }

        // Run action chains scheduled on earlier ticks that are now due (delayed effects resuming).
        await RunDueScheduledStackExecutionsAsync(now, ct);

        if (_triggers.IsEmpty)
        {
            // No wired triggers in the room: nothing can consume queued room events, so drop them
            // rather than let the queue grow unbounded. These were accepted while the index was
            // dirty -- membership is unknown until a rebuild -- so they are ignored rather than
            // dropped: nothing refused them, there was simply never anything to hand them to.
            for (int i = _eventQueue.Count; i > 0; i--)
            {
                Diagnostics.EventOutcome(WiredEventOutcome.IGNORED);
            }

            _eventQueue.Clear();

            return;
        }

        await ProcessTimedTriggersAsync(now, ct);

        int budget = Room.MaxEventsPerTick;

        while (budget-- > 0 && _eventQueue.Count > 0)
        {
            RoomEvent evt = _eventQueue.Dequeue();

            Diagnostics.EventOutcome(WiredEventOutcome.PROCESSED);

            await ProcessRoomEventAsync(evt, now, ct);
        }

        // Run the zero-delay chains just scheduled by this tick's fires, so trigger -> effect happens
        // within the same tick instead of one tick (~50ms) later.
        await RunDueScheduledStackExecutionsAsync(now, ct);
    }

    private async Task ProcessTimedTriggersAsync(long now, CancellationToken ct)
    {
        // Index-based: the registry is stable for the duration of this pass (it is only rebuilt at the
        // top of the tick), so an index loop is safe across the awaits below.
        for (int i = 0; i < _triggers.Timed.Count; i++)
        {
            FurnitureWiredTriggerLogic trigger = _triggers.Timed[i];

            if (trigger is not IWiredTimedTrigger timed)
            {
                continue;
            }

            // A box lingering in the registry after being picked up: skip it and reindex next tick.
            if (!Room.HasItem(trigger.ObjectId))
            {
                _triggers.MarkDirty();

                continue;
            }

            if (!timed.TryConsumeDue(now))
            {
                continue;
            }

            // Resolve the pile the trigger currently sits on, live. If it was dragged onto an empty
            // tile the pile has no actions and nothing fires — the "same pile" rule, for free.
            WiredStack stack = await _stacks.BuildFromTileAsync(trigger.TileIdx, ct);

            await FireTriggerWithEventAsync(
                trigger,
                new PeriodicRoomEvent
                {
                    RoomId = Room.RoomId,
                    CausedBy = ActionContext.CreateForWired(Room.RoomId),
                },
                stack,
                now,
                ct
            );
        }
    }

    /// <summary>
    /// Server side of the Timer Reset effect (<c>wf_act_reset_timers</c>): restart every resettable timed
    /// trigger in the room — repeaters re-anchor (fire next tick, interval afresh) and "at given time"
    /// one-shots re-arm so they can fire again. Room-wide, matching Habbo (the effect takes the room, not
    /// the pile).
    /// </summary>
    public void ResetTimers()
    {
        foreach (FurnitureWiredTriggerLogic trigger in _triggers.Timed)
        {
            if (trigger is IWiredResettableTimer resettable)
            {
                resettable.ResetTimer(_currentTickMs);
            }
        }
    }

    private async Task ProcessRoomEventAsync(RoomEvent evt, long now, CancellationToken ct)
    {
        if (evt is null)
        {
            return;
        }

        // A snapshot, because firing an action can mutate room furniture and a stale registry entry
        // marks the index dirty — iterating the live list would be iterating something this loop is
        // changing.
        foreach (FurnitureWiredTriggerLogic trigger in _triggers.Listening(evt.GetType()))
        {
            if (!Room.HasItem(trigger.ObjectId))
            {
                _triggers.MarkDirty();

                continue;
            }

            WiredStack stack = await _stacks.BuildFromTileAsync(trigger.TileIdx, ct);

            await FireTriggerWithEventAsync(trigger, evt, stack, now, ct);
        }
    }

    private async Task FireTriggerWithEventAsync(
        IWiredTrigger trigger,
        RoomEvent evt,
        IWiredStack stack,
        long now,
        CancellationToken ct
    )
    {
        if (
            trigger is null
            || evt is null
            || stack is null
            || !await trigger.MatchesEventAsync(evt, ct)
        )
        {
            return;
        }

        WiredProcessingContext ctx = new(_host)
        {
            Event = evt,
            Stack = stack,
            Trigger = trigger,
            Signal = BuildSignalSelection(evt),
        };

        if (evt.CausedBy.Origin == ActionOrigin.Player && evt.CausedBy.PlayerId > 0)
        {
            ctx.Selected.SelectedPlayerIds.Add(evt.CausedBy.PlayerId);
        }

        IWiredSelectionSet selection = await ctx.GetWiredSelectionSetAsync(trigger, ct);

        ctx.Selected.UnionWith(selection);

        foreach (IWiredSelector selector in ctx.Stack.Selectors)
        {
            IWiredSelectionSet set = await selector.SelectAsync(ctx, ct);

            ctx.SelectorPool.UnionWith(set);
        }

        foreach (IWiredAddon addon in ctx.Stack.Addons)
        {
            try
            {
                await addon.MutatePolicyAsync(ctx, ct);
            }
            catch (Exception ex)
            {
                Diagnostics.Logger.LogWarning(
                    ex,
                    "Wired addon {AddonType} failed to mutate the policy in room {RoomId}.",
                    addon.GetType().Name,
                    Room.RoomId
                );
            }
        }

        foreach (IWiredCondition condition in ctx.Stack.Conditions)
        {
            try
            {
                await condition.PrepareAsync(ctx, ct);
            }
            catch (Exception ex)
            {
                Diagnostics.Logger.LogWarning(
                    ex,
                    "Wired condition {ConditionType} failed to prepare in room {RoomId}; it will be evaluated without its data.",
                    condition.GetType().Name,
                    Room.RoomId
                );
            }
        }

        // The trigger is asked before the conditions, not after: a pile's negative actions are for
        // "the trigger fired but the conditions did not hold", so the two answers have to be
        // distinguishable. It also means a trigger that seals the triggering user into the
        // selection has done so before the conditions read it.
        if (!await trigger.CanTriggerAsync(ctx, ct))
        {
            return;
        }

        // The limit is asked after the add-ons have spoken (they are what sets it) and before any
        // branch runs, so a pile that is over its quota costs nothing at all this firing.
        if (!_policy.TryConsumeAllowance(ctx.Stack.StackId, ctx.Policy, now))
        {
            return;
        }

        bool conditionsPassed = EvaluateConditions(ctx.Stack.Conditions, ctx);

        ctx.Trigger?.FlashActivationStateAsync(ct)
            .LogAndForget(Diagnostics.Logger, "Failed to flash activation state for trigger.");

        // Before/AfterEffects addon hooks run in ExecuteStackChainAsync, around the chain's actual
        // execution — which can be ticks later than this scheduling when actions carry delays.
        ScheduleStackExecution(ctx, now, conditionsPassed);
    }

    /// <summary>Builds the signal payload for a fired stack: the furni and users a receive-signal
    /// trigger was handed by the send-signal action. Empty for every other event.</summary>
    private static WiredSelectionSet BuildSignalSelection(RoomEvent evt)
    {
        WiredSelectionSet signal = new();

        if (evt is SignalRoomEvent signalEvt)
        {
            signal.SelectedFurniIds.UnionWith(signalEvt.FurniIds);
            signal.SelectedPlayerIds.UnionWith(signalEvt.PlayerIds);
        }

        return signal;
    }

    private void ScheduleStackExecution(
        WiredProcessingContext ctx,
        long dueAtMs,
        bool conditionsPassed
    )
    {
        // ctx.Stack was resolved live from the trigger's current tile, so every action in it is already
        // co-located with the trigger. Delayed actions are re-validated again at execution time in
        // ExecuteStackChainAsync, in case a box leaves the pile during its delay window.
        List<IWiredAction> actions = _policy.ChooseActions(
            ctx.Stack.StackId,
            WiredActionBranch.Select(ctx.Stack.Actions, conditionsPassed),
            ctx.Policy
        );

        if (actions.Count == 0)
        {
            return;
        }

        WiredPendingStackExecution pending = new()
        {
            Stack = ctx.Stack,
            Actions = actions,
            Trigger = ctx.Trigger,
            Policy = ctx.Policy,
            Selected = ctx.Selected,
            SelectorPool = ctx.SelectorPool,
            Signal = ctx.Signal,
            ProcessingContext = ctx,
            NextActionIndex = 0,
        };

        // MaxScheduledPerTick bounds what a tick runs; MaxPendingExecutions bounds what accumulates
        // when a room schedules faster than it drains. Sixty-four events each arranging two delayed
        // chains, against sixty-four drained, grows the set by about 1,280 entries a second -- and
        // each one holds the pile, its actions, the selection, the selector pool and the processing
        // context. It was the cheapest room in the hotel to exhaust memory from, and a player could
        // build it.
        if (_schedule.IsFull(Room.MaxPendingExecutions))
        {
            _droppedExecutionCount++;
            Diagnostics.ChainStopped(WiredStopReason.SCHEDULE_DROP);

            return;
        }

        _schedule.Schedule(ctx.Stack.StackId, pending, dueAtMs);
    }

    private Task RunDueScheduledStackExecutionsAsync(long now, CancellationToken ct) =>
        _schedule.DrainDueAsync(
            now,
            Room.MaxScheduledPerTick,
            (key, pending) => ExecuteStackChainAsync(key, pending, now, ct)
        );

    private async Task<bool> ExecuteStackChainAsync(
        WiredExecutionKey key,
        WiredPendingStackExecution pending,
        long now,
        CancellationToken ct
    )
    {
        if (!pending.EffectsStarted)
        {
            pending.EffectsStarted = true;

            await RunAddonEffectHooksAsync(pending, before: true, ct);
        }

        for (int i = pending.NextActionIndex; i < pending.Actions.Count; i++)
        {
            IWiredAction action = pending.Actions[i];

            if (pending.WaitingActionIndex == i)
            {
                if (now < pending.DueAtMs)
                {
                    return false;
                }

                pending.WaitingActionIndex = null;
            }
            else
            {
                int delayMs = Math.Max(0, action.GetDelayMs());

                if (delayMs > 0)
                {
                    pending.WaitingActionIndex = i;

                    _schedule.Reschedule(key, pending, now + delayMs);

                    return false;
                }
            }

            // Re-validate co-location at execution time. A zero-delay action was resolved live moments
            // ago, but a delayed action may have been dragged off the trigger's tile (or picked up)
            // during its delay window — Habbo only lets a trigger drive actions on its own pile, so
            // such an action must not fire. key.StackId is the tile the trigger fired from.
            if (
                action is FurnitureWiredLogic actionBox
                && (!Room.HasItem(actionBox.ObjectId) || !IsOnTile(actionBox.ObjectId, key.StackId))
            )
            {
                // Said out loud, in both channels. This is the one refusal that used to happen in
                // complete silence, and it is the one whose report reads "my wired stopped working":
                // the box is still in the room, still configured, still stacked with something -- it
                // simply is not stacked with the trigger any more.
                Diagnostics.ChainStopped(WiredStopReason.REVALIDATION);

                WriteWiredRoomLog(
                    WiredLogLevel.Warning,
                    WiredLogSource.Action,
                    $"Action {actionBox.ObjectId.Value} did not run: it left the trigger's tile during its delay."
                );

                pending.NextActionIndex = i + 1;

                continue;
            }

            try
            {
                WiredExecutionContext ctx = new(_host)
                {
                    Addons = pending.Stack.Addons,
                    Policy = pending.Policy,
                    Selected = new WiredSelectionSet().UnionWith(pending.Selected),
                    SelectorPool = new WiredSelectionSet().UnionWith(pending.SelectorPool),
                    Signal = new WiredSelectionSet().UnionWith(pending.Signal),
                };

                action
                    .FlashActivationStateAsync(ct)
                    .LogAndForget(
                        Diagnostics.Logger,
                        "Failed to flash activation state for action."
                    );

                // What the event that started this chain says, under the names the @event.…
                // variables read. Done here rather than in each trigger: the pending execution kept
                // the firing context, which kept the event.
                WiredEventReadings.Populate(ctx, pending.ProcessingContext.Event);

                // Published for the duration of the action so the context variables have something
                // to read, and taken down again whatever happens — see CurrentContext.
                CurrentContext = ctx;
                _currentContextVariables = pending.ContextVariables;

                try
                {
                    await action.ExecuteAsync(ctx, ct);
                }
                finally
                {
                    CurrentContext = null;
                    _currentContextVariables = null;
                }

                FlushWiredContextAsync(ctx)
                    .LogAndForget(Diagnostics.Logger, "Failed to flush wired execution context.");

                WriteWiredRoomLog(
                    WiredLogLevel.Info,
                    WiredLogSource.Action,
                    $"Action {action.GetType().Name} executed for stack {key}."
                );
            }
            catch (Exception ex)
            {
                Diagnostics.Logger.LogWarning(
                    ex,
                    "Failed to execute pending wired action {ActionIndex} for stack {StackKey} in room {RoomId}.",
                    i,
                    key,
                    Room.RoomId
                );

                RecordWiredErrorLog(ex, action, now);

                WriteWiredRoomLog(
                    WiredLogLevel.Error,
                    WiredLogSource.Action,
                    $"Action {action.GetType().Name} failed for stack {key}: {ex.GetType().Name}."
                );
            }

            pending.NextActionIndex = i + 1;
        }

        await RunAddonEffectHooksAsync(pending, before: false, ct);

        return true;
    }

    private async Task RunAddonEffectHooksAsync(
        WiredPendingStackExecution pending,
        bool before,
        CancellationToken ct
    )
    {
        foreach (IWiredAddon addon in pending.Stack.Addons)
        {
            try
            {
                if (before)
                {
                    await addon.BeforeEffectsAsync(pending.ProcessingContext, ct);
                }
                else
                {
                    await addon.AfterEffectsAsync(pending.ProcessingContext, ct);
                }
            }
            catch (Exception ex)
            {
                Diagnostics.Logger.LogWarning(
                    ex,
                    "Wired addon {AddonType} {Hook} hook failed in room {RoomId}.",
                    addon.GetType().Name,
                    before ? "BeforeEffects" : "AfterEffects",
                    Room.RoomId
                );
            }
        }
    }

    /// <summary>Marks a wired box as lit; the wired tick reverts it to unlit after
    /// <c>WiredFlashDurationMs</c>. Re-flashing an already-lit box pushes its revert back.</summary>
    public void ScheduleFlashRevert(RoomObjectId objectId)
    {
        _flashRevertAtMs[objectId] = _currentTickMs + Room.FlashDurationMs;
    }

    private async Task ProcessFlashRevertsAsync(long now, CancellationToken ct)
    {
        if (_flashRevertAtMs.Count == 0)
        {
            return;
        }

        List<RoomObjectId>? due = null;

        foreach ((RoomObjectId objectId, long revertAtMs) in _flashRevertAtMs)
        {
            if (revertAtMs <= now)
            {
                (due ??= []).Add(objectId);
            }
        }

        if (due is null)
        {
            return;
        }

        foreach (RoomObjectId objectId in due)
        {
            _flashRevertAtMs.Remove(objectId);

            // A box picked up (or replaced) while lit simply has no revert to apply.
            if (
                !Room.TryGetItem(objectId, out IRoomItem? item)
                || item.Logic is not FurnitureWiredLogic wiredLogic
            )
            {
                continue;
            }

            try
            {
                await wiredLogic.SetFlashStateAsync(0);
            }
            catch (Exception ex)
            {
                Diagnostics.Logger.LogWarning(
                    ex,
                    "Failed to revert the flash state of wired item {ItemId} in room {RoomId}.",
                    objectId,
                    Room.RoomId
                );
            }
        }
    }

    private void RecordWiredErrorLog(Exception ex, IWiredAction action, long now) =>
        Diagnostics.RecordError(ex.GetType().Name, action.GetType().Name, now);

    internal void WriteWiredRoomLog(WiredLogLevel level, WiredLogSource source, string message)
    {
        Diagnostics.WriteRoomLog(
            new RoomWiredLogEntry
            {
                RoomId = Room.RoomId.Value,
                LogLevel = level,
                LogSource = source,
                Message = message,
                // Zero when the line was not written from inside an execute-stacks chain, which is
                // most of them. A reader filtering on a non-zero id gets one chain's chronology out
                // of a log that otherwise interleaves every pile in the room.
                ExecutionId = _callChain.CurrentExecutionId,
                ParentExecutionId = _callChain.ParentExecutionId,
            }
        );
    }

    private Task FlushWiredContextAsync(WiredExecutionContext ctx)
    {
        if (
            ctx.UserMoves.Count > 0
            || ctx.UserDirections.Count > 0
            || ctx.FloorItemMoves.Count > 0
            || ctx.WallItemMoves.Count > 0
        )
        {
            ctx.SendComposerToRoomAsync(
                    new WiredMovementsMessageComposer
                    {
                        Users = ctx.UserMoves,
                        FloorItems = ctx.FloorItemMoves,
                        WallItems = ctx.WallItemMoves,
                        UserDirections = ctx.UserDirections,
                    }
                )
                .LogAndForget(Diagnostics.Logger, "Failed to broadcast wired movements.");
        }

        if (ctx.FloorItemStateUpdates.Count > 0)
        {
            ctx.SendComposerToRoomAsync(
                    new ObjectsDataUpdateMessageComposer { StuffDatas = ctx.FloorItemStateUpdates }
                )
                .LogAndForget(Diagnostics.Logger, "Failed to broadcast floor item state updates.");
        }

        if (ctx.WallItemStateUpdates.Count > 0)
        {
            ctx.SendComposerToRoomAsync(
                    new ItemsStateUpdateMessageComposer { ObjectStates = ctx.WallItemStateUpdates }
                )
                .LogAndForget(Diagnostics.Logger, "Failed to broadcast wall item state updates.");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Runs the piles sitting under the given furni, as the "execute stacks" action asks: their
    /// triggers and their conditions are bypassed entirely, which is what the furni promises in as
    /// many words.
    /// </summary>
    /// <remarks>
    /// The calling pile's selection carries into the called one before its own selectors run, so
    /// "the user who walked on the first pile" is still who the second pile acts on. The caller's
    /// own tile is held in the chain guard for the duration, so a pile cannot execute itself.
    /// </remarks>
    /// <returns>How many piles were actually executed.</returns>
    internal async Task<int> ExecuteStacksAtAsync(
        int callerTileIdx,
        IReadOnlyCollection<int> targetFurniIds,
        IWiredSelectionSet inheritedSelection,
        CancellationToken ct
    )
    {
        if (targetFurniIds.Count == 0)
        {
            return 0;
        }

        if (!_callChain.HasRoomToDescend())
        {
            return 0;
        }

        // The caller's own tile is held for the duration, so a pile cannot execute itself.
        using WiredCallChainGuard.Hold caller = _callChain.Enter(callerTileIdx);

        int executed = 0;

        foreach (int furniId in targetFurniIds)
        {
            if (!Room.TryGetItem(furniId, out IRoomItem? item) || item is not IRoomFloorItem floor)
            {
                continue;
            }

            int tileIdx = Room.ToIdx(floor.X, floor.Y);

            using WiredCallChainGuard.Hold target = _callChain.Enter(tileIdx);

            if (target.IsCycle)
            {
                continue;
            }

            if (await ExecuteCalledStackAsync(tileIdx, inheritedSelection, ct))
            {
                executed++;
            }
        }

        return executed;
    }

    private async Task<bool> ExecuteCalledStackAsync(
        int tileIdx,
        IWiredSelectionSet inheritedSelection,
        CancellationToken ct
    )
    {
        WiredStack stack = await _stacks.BuildFromTileAsync(tileIdx, ct);

        if (stack.Actions.Count == 0)
        {
            return false;
        }

        WiredProcessingContext ctx = new(_host)
        {
            // No trigger and no triggering event: this pile ran because another one said so.
            Event = new WiredStackCalledEvent
            {
                RoomId = Room.RoomId,
                CausedBy = ActionContext.CreateForWired(Room.RoomId),
            },
            Stack = stack,
            Trigger = null,
        };

        ctx.Selected.UnionWith(inheritedSelection);

        foreach (IWiredSelector selector in stack.Selectors)
        {
            ctx.SelectorPool.UnionWith(await selector.SelectAsync(ctx, ct));
        }

        foreach (IWiredAddon addon in stack.Addons)
        {
            try
            {
                await addon.MutatePolicyAsync(ctx, ct);
            }
            catch (Exception ex)
            {
                Diagnostics.Logger.LogWarning(
                    ex,
                    "Wired addon {AddonType} failed to mutate the policy of a called pile in room {RoomId}.",
                    addon.GetType().Name,
                    Room.RoomId
                );
            }
        }

        // Conditions are deliberately not evaluated, so the positive branch is what runs.
        ScheduleStackExecution(ctx, Room.NowMs(), conditionsPassed: true);

        return true;
    }

    /// <summary>True if the given object currently sits on <paramref name="tileIdx"/>'s floor pile.</summary>
    private bool IsOnTile(RoomObjectId objectId, int tileIdx) =>
        _stacks.IsOnTile(objectId, tileIdx);

    private static bool EvaluateConditions(
        List<IWiredCondition> conditions,
        WiredProcessingContext ctx
    )
    {
        if (conditions.Count == 0)
        {
            return true;
        }

        // Every condition is evaluated, not short-circuited: the counting modes need the exact number
        // that passed, and conditions are pure predicates over room state.
        int matched = 0;

        foreach (IWiredCondition condition in conditions)
        {
            if (condition.Evaluate(ctx))
            {
                matched++;
            }
        }

        int total = conditions.Count;
        int target = ctx.Policy.ConditionCompareValue;

        return ctx.Policy.ConditionMode switch
        {
            WiredConditionModeType.All => matched == total,
            WiredConditionModeType.AtLeastOne => matched > 0,
            WiredConditionModeType.NotAll => matched < total,
            WiredConditionModeType.None => matched == 0,
            WiredConditionModeType.CountLessThan => matched < target,
            WiredConditionModeType.CountExactly => matched == target,
            WiredConditionModeType.CountMoreThan => matched > target,
            _ => matched == total,
        };
    }
}
