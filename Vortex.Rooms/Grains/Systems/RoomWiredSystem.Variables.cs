using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vortex.Primitives.Rooms.Enums.Wired;
using Vortex.Primitives.Rooms.Events;
using Vortex.Primitives.Rooms.Object.Furniture;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Primitives.Rooms.Snapshots.Wired;
using Vortex.Primitives.Rooms.Snapshots.Wired.Variables;
using Vortex.Primitives.Rooms.Wired;
using Vortex.Primitives.Rooms.Wired.Variable;
using Vortex.Protocol.Messages.Outgoing.Userdefinedroomevents;
using Vortex.Rooms.Grains.Storage;
using Vortex.Rooms.Object.Logic.Furniture.Floor.Wired.Variables;
using Vortex.Rooms.Wired;
using Vortex.Rooms.Wired.Variables;

namespace Vortex.Rooms.Grains.Systems;

public sealed partial class RoomWiredSystem
{
    private readonly HashSet<int> _dirtyVariableBoxIds = [];

    private readonly FurnitureActiveStore _furnitureActiveStore = new();
    private readonly PlayerActiveStore _playerActiveStore = new();
    private readonly RoomActiveStore _roomActiveStore = new();
    private readonly Dictionary<WiredVariableId, IWiredVariable> _variableById = [];

    /// <summary>
    /// Every variable a box put in the room, so removing the box removes all of them.
    /// </summary>
    /// <remarks>
    /// A list rather than one id: an add-on stacked with a variable box adds variables derived from
    /// it — level and progress from an experience value, year and hour from a timestamp, a Variable
    /// FX display from anything. They belong to the box, not to the add-on, because the box is what
    /// the room tracks and what a pickup takes away.
    /// </remarks>
    private readonly Dictionary<int, List<WiredVariableId>> _variableIdBoxId = [];

    /// <summary>
    /// The Variable FX displays the room has declared, by the variable each one draws.
    /// </summary>
    /// <remarks>
    /// Kept beside the variable registry rather than in it: a health bar is not readable as a
    /// variable and has no business being resolvable as one. Keyed by variable because that is the
    /// question asked on every value change, which is the hot path — the box that declared it is
    /// only ever asked about when the room is rebuilding the set.
    /// </remarks>
    private readonly Dictionary<
        WiredVariableId,
        List<WiredVariableFxConfigSnapshot>
    > _fxByVariable = [];

    private readonly Dictionary<int, List<WiredVariableId>> _fxVariablesByBox = [];

    private WiredVariablesSnapshot? _variablesSnapshot;

    public IWiredVariable? GetVariableById(WiredVariableId id)
    {
        if (_variableById.TryGetValue(id, out IWiredVariable? variable))
        {
            return variable;
        }

        return null;
    }

    public bool TryGetStoreForKey(WiredVariableKey key, out KeyValueStore? store)
    {
        return key.TargetType switch
        {
            WiredVariableTargetType.Furni => _furnitureActiveStore.TryGetStore(key, out store),
            WiredVariableTargetType.User => _playerActiveStore.TryGetStore(key, out store),
            WiredVariableTargetType.Global => _roomActiveStore.TryGetStore(key, out store),
            // Scoped to the chain running right now, and to nothing else. Asked outside a chain --
            // the wired menu listing what the room holds, a condition evaluated before the effects
            // begin -- there is no run to describe, so there is no store and the read is a miss.
            // It used to be the `_ =>` arm below, which threw on every read and write of the two
            // Context-typed boxes the client offers.
            WiredVariableTargetType.Context => (store = _currentContextVariables) is not null,
            _ => throw new ArgumentOutOfRangeException(
                nameof(key.TargetType),
                $"Unsupported target type: {key.TargetType}"
            ),
        };
    }

    public Task<WiredVariablesSnapshot> GetWiredVariablesSnapshotAsync(CancellationToken ct)
    {
        return Task.FromResult(_variablesSnapshot ??= BuildVariablesSnapshot());
    }

    public Task<
        List<(WiredVariableId id, WiredVariableValue value)>
    > GetAllVariablesForBindingAsync(WiredVariableBinding binding, CancellationToken ct)
    {
        List<(WiredVariableId id, WiredVariableValue value)> variableValues = new();

        foreach ((WiredVariableId id, IWiredVariable variable) in _variableById)
        {
            WiredVariableKey key = new(id, binding.TargetType, binding.TargetId);

            if (!variable.TryGetValue(key, out WiredVariableValue value))
            {
                continue;
            }

            variableValues.Add((id, value));
        }

        return Task.FromResult(variableValues);
    }

    /// <summary>Finds a room-scoped runtime variable by its display name and enumerates the
    /// current holders (players/furni in this room with a stored value), for the wired-menu
    /// "highlight holders" panel. Distinct from the persisted permanent-variable store — this
    /// reads live in-memory bindings only.</summary>
    public Task<(
        WiredVariableSnapshot Variable,
        List<(int ObjectId, int Value)> Holders
    )?> GetVariableHoldersByNameAsync(string variableName, CancellationToken ct)
    {
        IWiredVariable? variable = _variableById.Values.FirstOrDefault(v =>
            v.GetVarSnapshot().VariableName == variableName
        );

        if (variable is null)
        {
            return Task.FromResult<(WiredVariableSnapshot, List<(int, int)>)?>(null);
        }

        WiredVariableSnapshot snapshot = variable.GetVarSnapshot();
        List<(int ObjectId, int Value)> holders = new();

        IEnumerable<int> candidateIds = snapshot.TargetType switch
        {
            WiredVariableTargetType.User => Room.AllAvatarPlayerIds(),
            WiredVariableTargetType.Furni => Room.AllItemIds(),
            _ => [0],
        };

        foreach (int targetId in candidateIds)
        {
            WiredVariableKey key = new(snapshot.VariableId, snapshot.TargetType, targetId);

            if (variable.TryGetValue(key, out WiredVariableValue value))
            {
                holders.Add((targetId, value));
            }
        }

        return Task.FromResult<(WiredVariableSnapshot, List<(int, int)>)?>((snapshot, holders));
    }

    private Task ProcessInternalVariablesAsync(long now, CancellationToken ct)
    {
        IEnumerable<IWiredVariable> variables = _host.InternalVariables();

        foreach (IWiredVariable variable in variables)
        {
            ProcessVariable(variable);
        }

        return Task.CompletedTask;
    }

    private async Task ProcessVariableBoxesAsync(long now, CancellationToken ct)
    {
        if (_dirtyVariableBoxIds.Count == 0)
        {
            return;
        }

        List<int> dirtyVariableBoxIds = _dirtyVariableBoxIds.ToList();
        _dirtyVariableBoxIds.Clear();

        foreach (int boxId in dirtyVariableBoxIds)
        {
            await ProcessVariableBoxAsync(boxId, ct);
        }

        _variablesSnapshot = null;

        await PublishFxConfigsAsync();
    }

    private async Task ProcessVariableBoxAsync(int boxId, CancellationToken ct)
    {
        RemoveVariableBox(boxId);

        if (
            !Room.TryGetItem(boxId, out IRoomItem? item)
            || item.Logic is not FurnitureWiredVariableLogic variable
        )
        {
            return;
        }

        await variable.LoadWiredAsync(ct);

        if (!ProcessVariable(variable))
        {
            return;
        }

        List<WiredVariableId> registered = [variable.GetVarSnapshot().VariableId];

        foreach (IWiredVariable derived in await BuildSubVariablesAsync(item, variable, ct))
        {
            if (ProcessVariable(derived))
            {
                registered.Add(derived.GetVarSnapshot().VariableId);
            }
        }

        _variableIdBoxId[boxId] = registered;
    }

    /// <summary>
    /// The variables the add-ons stacked with this box derive from it.
    /// </summary>
    /// <remarks>
    /// The pile is resolved from the box's own tile, which is also how a firing stack finds its
    /// add-ons — the resolver leaves variable boxes out of a pile deliberately, but the add-ons
    /// standing with them are exactly what this needs.
    /// <para>
    /// Each contributing add-on is hydrated first. An add-on the room has not loaded yet answers
    /// from a blank configuration, which produces a set of sub-variables named after nothing, and
    /// they would sit in the registry until the next time the box happened to go dirty.
    /// </para>
    /// </remarks>
    private async Task<List<IWiredVariable>> BuildSubVariablesAsync(
        IRoomItem box,
        FurnitureWiredVariableLogic parent,
        CancellationToken ct
    )
    {
        List<IWiredVariable> derived = [];

        if (box is not IRoomFloorItem floor)
        {
            return derived;
        }

        WiredStack stack = await _stacks.BuildFromTileAsync(Room.ToIdx(floor.X, floor.Y), ct);

        foreach (IWiredAddon addon in stack.Addons)
        {
            if (addon is not IWiredSubVariableSource and not IWiredVariableFxSource)
            {
                continue;
            }

            try
            {
                await addon.LoadWiredAsync(ct);

                if (addon is IWiredSubVariableSource source)
                {
                    derived.AddRange(source.CreateSubVariables(parent));
                }

                if (
                    addon is IWiredVariableFxSource fx
                    && fx.CreateFxConfig(parent) is WiredVariableFxConfigSnapshot config
                )
                {
                    AddFxConfig(box.ObjectId.Value, parent.GetVarSnapshot().VariableId, config);
                }
            }
            catch (Exception ex)
            {
                Diagnostics.Logger.LogWarning(
                    ex,
                    "Wired add-on {AddonType} failed to describe variable {BoxId} in room {RoomId}.",
                    addon.GetType().Name,
                    box.ObjectId.Value,
                    Room.RoomId
                );
            }
        }

        return derived;
    }

    private void AddFxConfig(
        int boxId,
        WiredVariableId variableId,
        WiredVariableFxConfigSnapshot config
    )
    {
        if (
            !_fxByVariable.TryGetValue(variableId, out List<WiredVariableFxConfigSnapshot>? configs)
        )
        {
            configs = [];
            _fxByVariable[variableId] = configs;
        }

        configs.Add(config);

        if (!_fxVariablesByBox.TryGetValue(boxId, out List<WiredVariableId>? variables))
        {
            variables = [];
            _fxVariablesByBox[boxId] = variables;
        }

        variables.Add(variableId);
    }

    /// <summary>Drops the displays a box declared, for the same reason its variables go: the add-on
    /// that described them is on the box's tile and leaves with it.</summary>
    private void RemoveFxConfigs(int boxId)
    {
        if (!_fxVariablesByBox.TryGetValue(boxId, out List<WiredVariableId>? variables))
        {
            return;
        }

        _fxVariablesByBox.Remove(boxId);

        foreach (WiredVariableId variableId in variables)
        {
            _fxByVariable.Remove(variableId);
        }
    }

    /// <summary>
    /// Tells the room which displays exist, as one set.
    /// </summary>
    /// <remarks>
    /// The client replaces its config table from what this carries, so a config left out is a config
    /// the room no longer has — which is exactly what should happen when a box is picked up. Sending
    /// nothing when there are none is therefore not a waste: it is how the last display is removed.
    /// </remarks>
    private Task PublishFxConfigsAsync() =>
        _host.Actions.SendComposerToRoomAsync(
            new VariableFxConfigUpdateMessageComposer
            {
                Configs = [.. _fxByVariable.Values.SelectMany(configs => configs)],
            }
        );

    /// <summary>
    /// Pushes what the displays bound to this variable now read, for the one entity that changed.
    /// </summary>
    /// <remarks>
    /// Driven off the same event the "variable changed" trigger listens for, so a value that moves
    /// without passing through there would not update a bar either — one path, not two that can
    /// disagree.
    /// </remarks>
    private Task PublishFxStatusAsync(WiredVariableChangedEvent evt)
    {
        if (
            !_fxByVariable.TryGetValue(
                evt.Key.VariableId,
                out List<WiredVariableFxConfigSnapshot>? configs
            )
        )
        {
            return Task.CompletedTask;
        }

        bool isUserEntity = evt.Key.TargetType == WiredVariableTargetType.User;
        string variableId = evt.Key.VariableId.ToString();

        return _host.Actions.SendComposerToRoomAsync(
            new VariableFxStatusUpdateMessageComposer
            {
                Statuses =
                [
                    .. configs.Select(config => new WiredVariableFxStatusSnapshot
                    {
                        StatusKey = WiredVariableFxKey.Build(
                            config.ConfigId,
                            variableId,
                            isUserEntity,
                            evt.Key.TargetId
                        ),
                        IsInitialize = evt.Kind == WiredVariableChangeKind.Created,
                        IsUserEntity = isUserEntity,
                        EntityId = evt.Key.TargetId,
                        Value = evt.Current,
                    }),
                ],
            }
        );
    }

    /// <summary>
    /// The same change, restated once for every reading derived from the variable that moved.
    /// </summary>
    /// <remarks>
    /// A level-up add-on's readings are variables the client offers in every picker, the "variable
    /// changed" trigger included — and the add-on exists for exactly one chain: fire when the level
    /// goes up. But a reading owns no value, so every write lands on the parent and the parent is
    /// the only thing that ever announces one. Without this the readings were pickable and
    /// permanently silent.
    /// <para>
    /// Both values are run through the reading, so the trigger's Increased / Decreased / Unchanged
    /// options ask about the level rather than about the experience behind it. A write that leaves
    /// the reading where it was is still announced — that is what the client's "Unchanged" option
    /// is for.
    /// </para>
    /// </remarks>
    private List<WiredVariableChangedEvent> DerivedChanges(WiredVariableChangedEvent evt)
    {
        List<WiredVariableChangedEvent> derivedChanges = [];

        // ponytail: a scan of the room's variables per change. A room holds tens of them, and an
        // index keyed by source would be one more thing to invalidate every time a box moves --
        // build one if a room ever holds enough variables for this to show.
        foreach (IWiredVariable variable in _variableById.Values)
        {
            if (
                variable is not IWiredDerivedVariable derived
                || derived.SourceVariableId != evt.Key.VariableId
            )
            {
                continue;
            }

            derivedChanges.Add(
                evt with
                {
                    Key = evt.Key with { VariableId = variable.GetVarSnapshot().VariableId },
                    Previous = derived.ValueFor(evt.Previous),
                    Current = derived.ValueFor(evt.Current),
                }
            );
        }

        return derivedChanges;
    }

    /// <summary>
    /// Announces what the time-driven variables did since the last tick, through the same door a
    /// written variable's change takes, so the "variable changed" trigger and the FX displays see
    /// both alike.
    /// </summary>
    /// <remarks>
    /// The changes are gathered before any is announced: announcing runs the room's derived-change
    /// scan over the same registry this walks.
    /// </remarks>
    private async Task PollVariablesAsync(long now, CancellationToken ct)
    {
        List<WiredVariableChangedEvent>? changes = null;

        foreach (IWiredVariable variable in _variableById.Values)
        {
            if (variable is IWiredPolledVariable polled)
            {
                (changes ??= []).AddRange(polled.Poll(now));
            }
        }

        foreach (WiredVariableChangedEvent change in changes ?? [])
        {
            await OnRoomEventAsync(change, ct);
        }
    }

    private bool ProcessVariable(IWiredVariable variable)
    {
        WiredVariableSnapshot snapshot = variable.GetVarSnapshot();

        if (string.IsNullOrWhiteSpace(snapshot.VariableName))
        {
            return false;
        }

        _variableById[snapshot.VariableId] = variable;

        return true;
    }

    private void RemoveVariableBox(int boxId)
    {
        if (!_variableIdBoxId.TryGetValue(boxId, out List<WiredVariableId>? variableIds))
        {
            return;
        }

        RemoveFxConfigs(boxId);

        _variableIdBoxId.Remove(boxId);

        // Every id the box registered, not just its own: a box whose add-on derived four
        // sub-variables would otherwise leave four entries pointing at a logic the room has
        // dropped, and the next reader of the variable list would resolve them.
        foreach (WiredVariableId variableId in variableIds)
        {
            _variableById.Remove(variableId);
        }
    }

    private WiredVariablesSnapshot BuildVariablesSnapshot()
    {
        List<WiredVariableHash> hashes = new();
        List<WiredVariableSnapshot> snapshots = new(_variableById.Count);

        foreach (IWiredVariable variable in _variableById.Values)
        {
            WiredVariableSnapshot snapshot = variable.GetVarSnapshot();

            hashes.Add(snapshot.VariableHash);
            snapshots.Add(snapshot);
        }

        WiredVariablesSnapshot allVariablesSnapshot = new()
        {
            AllVariablesHash = WiredVariableHashBuilder.HashFromHashes(hashes),
            Variables = snapshots,
        };

        Room.AllVariablesHash = allVariablesSnapshot.AllVariablesHash;

        return allVariablesSnapshot;
    }
}
