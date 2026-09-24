using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;
using Vortex.Primitives.Action;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Rooms.Enums.Wired;
using Vortex.Primitives.Rooms.Events;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Furniture;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Primitives.Rooms.Snapshots.Wired;
using Vortex.Primitives.Rooms.Snapshots.Wired.Variables;
using Vortex.Primitives.Rooms.Wired;
using Vortex.Primitives.Rooms.Wired.Variable;
using Vortex.Protocol.Messages.Incoming.Userdefinedroomevents;
using Vortex.Protocol.Messages.Outgoing.Userdefinedroomevents;
using Vortex.Rooms.Wired;

namespace Vortex.Rooms.Object.Logic.Furniture.Floor.Wired;

public abstract class FurnitureWiredLogic(
    IGrainFactory grainFactory,
    IStuffDataFactory stuffDataFactory,
    IRoomFloorItemContext ctx
) : FurnitureFloorLogic(stuffDataFactory, ctx), IWiredBox
{
    protected readonly IGrainFactory _grainFactory = grainFactory;

    private WiredDataSnapshot? _snapshot;

    protected IWiredData _wiredData = null!;

    // Wired boxes hold durable player configuration (selected items, delays, conditions,
    // sources), which must survive a room unload / reboot, so a wired furni persists exactly
    // like any other furni (Persistent), not RoomActive. Note this covers the WIRED config
    // section only: the box's furni *state* is purely the activation blink and is deliberately
    // never persisted — see SetFlashStateAsync. Ephemeral runtime state (schedules, per-tick
    // "already triggered" counters) lives in the wired execution system, is never serialized
    // into WiredData, and is correctly lost on unload by design.
    protected override StuffPersistanceType _stuffPersistanceType =>
        StuffPersistanceType.Persistent;

    public abstract WiredType WiredType { get; }
    public abstract int WiredCode { get; }

    /// <summary>The room object id of this wired box, used by the wired system to skip a box that has been
    /// removed from the room (a stale trigger-registry entry) and reindex.</summary>
    public RoomObjectId ObjectId => _ctx.ObjectId;

    /// <summary>The tile this wired box currently sits on. The wired system resolves a trigger's pile live
    /// from this tile at fire time, so a box dragged to another tile drives that new tile's pile (and an
    /// empty tile fires nothing) — the "same pile" rule holds without any cached stack.</summary>
    public int TileIdx => _ctx.GetTileIdx();

    public async Task LoadWiredAsync(CancellationToken ct)
    {
        await FillInternalDataAsync(ct);
    }

    public Task FlashActivationStateAsync(CancellationToken ct)
    {
        // A real flash: light the box now, and let the wired system revert it to unlit after
        // WiredFlashDurationMs. A plain toggle would leave the box lit every other fire.
        _ctx.Furni.ScheduleFlashRevert(_ctx.ObjectId);

        return SetFlashStateAsync(1);
    }

    /// <summary>
    /// Sets the box's lit/unlit visual and broadcasts it, without persisting. The activation blink is
    /// the only thing a wired box's furni state is used for, and it is pure ephemeral runtime state:
    /// routing it through <see cref="FurnitureLogic.SetStateAsync"/> would rewrite the furni's
    /// <c>extra_data</c> on every fire *and* every revert — a periodic at 0.5s means several furni
    /// writes per second, forever — and would leave a box stuck lit if the room unloaded mid-flash,
    /// since the pending revert does not survive the unload.
    /// </summary>
    public Task SetFlashStateAsync(int state)
    {
        StuffData.SetState(state.ToString());

        return _ctx.RefreshStuffDataAsync();
    }

    public virtual List<int> GetStuffIds()
    {
        if (GetValidStuffIds(_wiredData.StuffIds, out List<int>? stuffIds))
        {
            if (!_wiredData.StuffIds.SequenceEqual(stuffIds))
            {
                _wiredData.StuffIds = stuffIds;

                _wiredData.MarkDirty();
            }
        }

        return stuffIds ?? [];
    }

    public virtual List<int> GetStuffIds2()
    {
        if (GetValidStuffIds(_wiredData.StuffIds2, out List<int>? stuffIds))
        {
            if (!_wiredData.StuffIds2.SequenceEqual(stuffIds))
            {
                _wiredData.StuffIds2 = stuffIds;

                _wiredData.MarkDirty();
            }
        }

        return stuffIds ?? [];
    }

    /// <summary>The furni recorded at the last save, pruned to the ones still in the room — a
    /// snapshot outlives the furni it describes, and selecting an id nothing answers for would put a
    /// ghost in every set built from it.</summary>
    public virtual List<int> GetSnapshotFurniIds() =>
        [
            .. _wiredData
                .Snapshots.Select(snapshot => snapshot.FurniId)
                .Where(id => _ctx.Lookup.TryFindItem(id, out _)),
        ];

    public virtual List<IWiredParamRule> GetIntParamRules()
    {
        return [];
    }

    public virtual IWiredParamRule? GetIntParamTailRule()
    {
        return null;
    }

    /// <summary>
    /// The part of the string parameter a player reads — a message the box shows, a line a bot
    /// says — which the hotel-wide word filter checks on save. Null, the default, when the string is
    /// not prose at all: a bot's name, a keyword a trigger listens for, a time zone, a variable's
    /// name. Those are keys, and filtering them would only break what they point at.
    /// </summary>
    public virtual (int Start, int Length)? ProseInStringParam(string stringParam) => null;

    /// <summary>
    /// The rule for every int param the box actually carries, tail included.
    /// </summary>
    /// <remarks>
    /// A box with a variable parameter count declares a handful of fixed rules and one
    /// <see cref="GetIntParamTailRule"/> standing for all the rest — the level-up add-on sends two
    /// ints for a manual curve, four for a linear one and five for an exponential one, so a fixed
    /// list cannot express it.
    /// <para>
    /// Normalisation and repair both honour that tail (<see cref="TryNormalizeIntParams"/>,
    /// <see cref="RepairIntParams"/>); attaching did not, and handed <see cref="WiredData"/> the
    /// fixed rules alone. `GetIntParam` looks the rule up *before* the value, so reading param 2 of
    /// a five-param box indexed a two-entry list and threw ArgumentOutOfRange on every hydration —
    /// which is once per room load, per box. The add-on's own `Param(index, fallback)` guard could
    /// not catch it: it checks the params, and it was the rules that were short.
    /// </para>
    /// </remarks>
    private IReadOnlyList<IWiredParamRule> BuildIntParamRules(int count)
    {
        List<IWiredParamRule> rules = GetIntParamRules();
        IWiredParamRule? tail = GetIntParamTailRule();

        if (tail is null || count <= rules.Count)
        {
            return rules;
        }

        rules.Capacity = count;

        while (rules.Count < count)
        {
            rules.Add(tail);
        }

        return rules;
    }

    public virtual List<WiredFurniSourceType[]> GetAllowedFurniSources()
    {
        return [];
    }

    /// <summary>
    /// Whether saving this box records where its furni stood and how they looked.
    /// </summary>
    /// <remarks>
    /// Mirrors the client's own <c>hasStateSnapshot</c>, which is a property of the box type and not
    /// of its configuration. The capture happens at save time on purpose: the interaction is
    /// "arrange the room, then save the box", and a snapshot taken when the box fires would only
    /// ever record the arrangement it is about to overwrite.
    /// </remarks>
    public virtual bool HasStateSnapshot => false;

    /// <summary>Where each of these furni stands right now, for the boxes that restore it later.
    /// A furni that is not a floor item is skipped: nothing else has a position to return to.
    /// </summary>
    protected List<WiredFurniStateSnapshot> CaptureSnapshots(List<int> furniIds)
    {
        List<WiredFurniStateSnapshot> snapshots = [];

        foreach (int furniId in furniIds)
        {
            if (
                !_ctx.Lookup.TryFindItem(furniId, out IRoomItem? item)
                || item is not IRoomFloorItem floorItem
            )
            {
                continue;
            }

            snapshots.Add(
                new WiredFurniStateSnapshot
                {
                    FurniId = furniId,
                    X = floorItem.X,
                    Y = floorItem.Y,
                    Z = floorItem.Z.ToInt(),
                    Rotation = (int)floorItem.Rotation,
                    State = item.Logic.GetState(),
                }
            );
        }

        return snapshots;
    }

    public virtual List<WiredPlayerSourceType[]> GetAllowedPlayerSources()
    {
        return [];
    }

    public virtual List<Type> GetDefinitionSpecificTypes()
    {
        return [];
    }

    public virtual List<Type> GetTypeSpecificTypes()
    {
        return [];
    }

    public virtual List<WiredVariableContextSnapshot> GetWiredContextSnapshots()
    {
        return [];
    }

    public List<WiredFurniSourceType[]> GetFurniSources()
    {
        List<WiredFurniSourceType[]> sources = new();
        int index = 0;

        foreach (WiredFurniSourceType[] source in GetDefaultFurniSources())
        {
            WiredFurniSourceType[] sourceTypes = source;

            // Defaults yield one slot per allowed source; the persisted list only holds
            // slots the player has configured (often fewer, empty when unconfigured), so
            // fall back to the default whenever this slot has no persisted override.
            if (index < _wiredData.FurniSources.Count && _wiredData.FurniSources[index] is not null)
            {
                sourceTypes = _wiredData.FurniSources[index];
            }

            sources.Add(sourceTypes);
            index++;
        }

        return sources;
    }

    public List<WiredPlayerSourceType[]> GetPlayerSources()
    {
        List<WiredPlayerSourceType[]> sources = new();
        int index = 0;

        foreach (WiredPlayerSourceType[] source in GetDefaultPlayerSources())
        {
            WiredPlayerSourceType[] sourceTypes = source;

            // Defaults yield one slot per allowed source; the persisted list only holds
            // slots the player has configured (often fewer, empty when unconfigured), so
            // fall back to the default whenever this slot has no persisted override.
            if (
                index < _wiredData.PlayerSources.Count
                && _wiredData.PlayerSources[index] is not null
            )
            {
                sourceTypes = _wiredData.PlayerSources[index];
            }

            sources.Add(sourceTypes);
            index++;
        }

        return sources;
    }

    public List<WiredFurniSourceType[]> GetDefaultFurniSources()
    {
        return [.. GetAllowedFurniSources().Select(x => new[] { x[0] })];
    }

    public List<WiredPlayerSourceType[]> GetDefaultPlayerSources()
    {
        return [.. GetAllowedPlayerSources().Select(x => new[] { x[0] })];
    }

    public virtual async Task<bool> ApplyWiredUpdateAsync(
        ActionContext ctx,
        WiredUpdateRequest update,
        CancellationToken ct
    )
    {
        try
        {
            // Guarantee the config is hydrated and the persistence callback is wired before
            // we mutate it. A client can send an update before the wired stack has been
            // processed (which is what normally triggers LoadWiredAsync); without this the
            // mutation would NRE on a null _wiredData, get swallowed, and the player's
            // configuration would be silently dropped instead of persisted.
            if (_wiredData is null)
            {
                await FillInternalDataAsync(ct);

                if (_wiredData is null)
                {
                    return false;
                }
            }

            List<int> intParams = new();
            string stringParam = update.StringParam;
            List<int> stuffIds = new();
            List<int> stuffIds2 = new();
            List<string> variableIds = new();
            List<WiredFurniSourceType[]> furniSources = new();
            List<WiredPlayerSourceType[]> playerSources = new();
            List<object> definitionSpecifics = new();
            List<object> typeSpecifics = new();

            if (TryNormalizeIntParams(update.IntParams, out List<int> normalizedIntParams))
            {
                intParams = normalizedIntParams;
            }
            else
            {
                return false;
            }

            if (GetValidStuffIds(update.StuffIds, out List<int> validStuffIds))
            {
                stuffIds = validStuffIds;
            }

            if (GetValidStuffIds(update.StuffIds2, out List<int> validStuffIds2))
            {
                stuffIds2 = validStuffIds2;
            }

            if (GetValidVariableIds(update.VariableIds, out List<WiredVariableId> validVariableIds))
            {
                variableIds = [.. validVariableIds.Select(x => x.ToString())];
            }

            int index = 0;
            List<WiredFurniSourceType[]> validFurniSources = GetAllowedFurniSources();

            foreach (WiredFurniSourceType[] source in GetDefaultFurniSources())
            {
                WiredFurniSourceType[]? sourceTypes = source;

                try
                {
                    if (update.FurniSources[index] is not null)
                    {
                        sourceTypes =
                        [
                            .. update
                                .FurniSources[index]
                                .Where(validFurniSources[index].Contains)
                                .Take(source.Length),
                        ];
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Malformed FurniSources[{Index}] in wired update for item {ItemId}; keeping default source.",
                        index,
                        _ctx.ObjectId
                    );
                }

                furniSources.Add(sourceTypes);
                index++;
            }

            index = 0;
            List<WiredPlayerSourceType[]> validPlayerSources = GetAllowedPlayerSources();

            foreach (WiredPlayerSourceType[] source in GetDefaultPlayerSources())
            {
                WiredPlayerSourceType[]? sourceTypes = source;

                try
                {
                    if (update.PlayerSources[index] is not null)
                    {
                        sourceTypes =
                        [
                            .. update
                                .PlayerSources[index]
                                .Where(validPlayerSources[index].Contains)
                                .Take(source.Length),
                        ];
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Malformed PlayerSources[{Index}] in wired update for item {ItemId}; keeping default source.",
                        index,
                        _ctx.ObjectId
                    );
                }

                playerSources.Add(sourceTypes);
                index++;
            }

            index = 0;

            foreach (Type specType in GetDefinitionSpecificTypes())
            {
                object specific = default!;

                try
                {
                    if (
                        update.DefinitionSpecifics[index] is not null
                        && specType.IsInstanceOfType(update.DefinitionSpecifics[index])
                    )
                    {
                        specific = update.DefinitionSpecifics[index];
                    }
                    else
                    {
                        specific = Activator.CreateInstance(specType)!;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to rehydrate definition-specific {Type} at index {Index} from wired update for item {ItemId}.",
                        specType,
                        index,
                        _ctx.ObjectId
                    );
                }

                definitionSpecifics.Add(specific);
                index++;
            }

            index = 0;

            foreach (Type specType in GetTypeSpecificTypes())
            {
                object specific = default!;

                try
                {
                    if (
                        update.TypeSpecifics[index] is not null
                        && specType.IsInstanceOfType(update.TypeSpecifics[index])
                    )
                    {
                        specific = update.TypeSpecifics[index];
                    }
                    else
                    {
                        specific = Activator.CreateInstance(specType)!;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to rehydrate type-specific {Type} at index {Index} from wired update for item {ItemId}.",
                        specType,
                        index,
                        _ctx.ObjectId
                    );
                }

                typeSpecifics.Add(specific);
                index++;
            }

            _wiredData.IntParams = intParams;
            _wiredData.StringParam = stringParam;
            _wiredData.StuffIds = stuffIds;
            _wiredData.StuffIds2 = stuffIds2;
            _wiredData.Snapshots = HasStateSnapshot ? CaptureSnapshots(stuffIds) : [];
            _wiredData.VariableIds = variableIds;
            _wiredData.FurniSources = furniSources;
            _wiredData.PlayerSources = playerSources;
            _wiredData.DefinitionSpecifics = definitionSpecifics;
            _wiredData.TypeSpecifics = typeSpecifics;

            _wiredData.MarkDirty();

            // The cached snapshot no longer reflects the config; without this a player reopening
            // the box before the next pile resolution would see the pre-update values.
            _snapshot = null;

            // Past the commit point. The configuration above is already written, marked dirty and
            // will be persisted; the notification is a reaction to it, not part of it. Letting it
            // fall into the catch below reported `false` -- which the handler reads as "refused" and
            // answers by withholding WiredSaveSuccess -- for a save that had in fact happened. The
            // player then reopened the box, saw their new configuration, and had been told it did
            // not take.
            try
            {
                await OnWiredStackChangedAsync(ctx, [_ctx.GetTileIdx()], ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Wired configuration for item {ItemId} was applied, but notifying the stack failed. The room's wired state is stale until the next pile resolution.",
                    _ctx.ObjectId
                );
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to apply wired update for item {ItemId}.",
                _ctx.ObjectId
            );
            return false;
        }
    }

    public WiredDataSnapshot GetSnapshot()
    {
        return _snapshot ??= BuildSnapshot();
    }

    public virtual int GetMaxVariableIds()
    {
        return 0;
    }

    public virtual bool SupportsAdvancedMode()
    {
        return true;
    }

    public List<object> GetDefinitionSpecifics() =>
        MaterializeSpecifics(
            _wiredData.DefinitionSpecifics,
            GetDefinitionSpecificTypes(),
            nameof(WiredData.DefinitionSpecifics)
        );

    public List<object> GetTypeSpecifics() =>
        MaterializeSpecifics(
            _wiredData.TypeSpecifics,
            GetTypeSpecificTypes(),
            nameof(WiredData.TypeSpecifics)
        );

    // Persisted slots survive the extra_data JSON round-trip as JsonElement, not as their declared
    // CLR types — the codec converts them back. A slot that cannot be materialized (truly malformed
    // data) falls back to a fresh default, loudly.
    private List<object> MaterializeSpecifics(
        List<object> stored,
        List<Type> slotTypes,
        string slotKind
    )
    {
        List<object> specifics = new();
        int index = 0;

        foreach (Type specType in slotTypes)
        {
            object? specific = null;

            if (index < stored.Count && stored[index] is not null)
            {
                if (WiredSpecificsCodec.TryMaterialize(stored[index], specType, out object? value))
                {
                    specific = value;
                }
                else
                {
                    _logger.LogWarning(
                        "Malformed persisted {SlotKind}[{Index}] ({Type}) for wired item {ItemId}; falling back to a fresh instance.",
                        slotKind,
                        index,
                        specType,
                        _ctx.ObjectId
                    );
                }
            }

            specific ??= Activator.CreateInstance(specType)!;

            specifics.Add(specific);
            index++;
        }

        return specifics;
    }

    public List<int> GetDefaultIntParams()
    {
        List<int> ints = new();

        foreach (IWiredParamRule rule in GetIntParamRules())
        {
            ints.Add(rule.DefaultValue);
        }

        return ints;
    }

    protected virtual bool TryNormalizeIntParams(List<int> proposed, out List<int> normalized)
    {
        normalized = [];

        List<IWiredParamRule> fixedRules = GetIntParamRules();
        IWiredParamRule? tailRule = GetIntParamTailRule();
        int min = fixedRules.Count;
        int max = Math.Max(min, _ctx.WiredLimits.WiredMaxIntParams);

        if (proposed.Count > max)
        {
            return false;
        }

        if (tailRule is null)
        {
            if (proposed.Count != fixedRules.Count)
            {
                return false;
            }

            for (int i = 0; i < fixedRules.Count; i++)
            {
                IWiredParamRule rule = fixedRules[i];

                try
                {
                    int v = proposed[i];

                    if (!rule.IsValid(v))
                    {
                        return false;
                    }

                    normalized.Add(rule.Sanitize(v));
                }
                catch
                {
                    normalized.Add(fixedRules[i].DefaultValue);
                }
            }

            return true;
        }

        if (proposed.Count < min)
        {
            return false;
        }

        for (int i = 0; i < fixedRules.Count; i++)
        {
            IWiredParamRule rule = fixedRules[i];
            int v = i < proposed.Count ? proposed[i] : rule.DefaultValue;

            if (i < proposed.Count && !rule.IsValid(v))
            {
                return false;
            }

            normalized.Add(rule.Sanitize(v));
        }

        for (int i = fixedRules.Count; i < proposed.Count; i++)
        {
            int v = proposed[i];

            if (!tailRule.IsValid(v))
            {
                return false;
            }

            normalized.Add(tailRule.Sanitize(v));
        }

        return true;
    }

    /// <summary>
    /// Hydration-time repair of this box's persisted int params, so a list that
    /// <see cref="TryNormalizeIntParams"/> would reject still comes up usable instead of leaving
    /// every <c>GetIntParam</c> in the leaf throwing on each fire. See
    /// <see cref="WiredIntParamRepair"/>.
    /// </summary>
    protected List<int> RepairIntParams(List<int> persisted) =>
        WiredIntParamRepair.Repair(
            GetIntParamRules(),
            GetIntParamTailRule(),
            _ctx.WiredLimits.WiredMaxIntParams,
            persisted
        );

    protected virtual bool GetValidStuffIds(List<int> proposed, out List<int> stuffIds)
    {
        stuffIds = [];

        int count = 0;

        foreach (int id in proposed)
        {
            if (!_ctx.Lookup.TryFindItem(id, out IRoomItem? item))
            {
                continue;
            }

            stuffIds.Add(id);

            count++;

            if (count >= _ctx.WiredLimits.WiredSelectedItemsLimit)
            {
                break;
            }
        }

        return true;
    }

    /// <summary>The client's sentinel for "no variable chosen".</summary>
    private const string NoVariable = "n";

    protected virtual bool GetValidVariableIds(
        List<string> proposed,
        out List<WiredVariableId> variableIds
    )
    {
        variableIds = [];

        int count = 0;
        int max = GetMaxVariableIds();

        foreach (string id in proposed)
        {
            // An unfilled slot is not a malformed one. The client sends a placeholder for every
            // variable a box *could* take, so a box with two optional amounts and neither of them
            // sourced from a variable sends two of these -- and every one of them used to arrive
            // as a parse failure with a stack trace behind it. "n" is the client's own
            // DEFAULT_VARIABLE_ID; the empty string is what the value-or-variable sections send
            // when nothing is picked.
            if (string.IsNullOrEmpty(id) || id == NoVariable)
            {
                continue;
            }

            try
            {
                WiredVariableId variableId = WiredVariableId.Parse(id);
                IWiredVariable? variable = _ctx.Furni.GetVariableById(variableId);

                if (variable is null)
                {
                    continue;
                }

                variableIds.Add(variableId);

                count++;

                if (count >= max)
                {
                    break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Malformed variable id {VariableId} for wired item {ItemId}; skipping it.",
                    id,
                    _ctx.ObjectId
                );
            }
        }

        return true;
    }

    protected virtual Task FillInternalDataAsync(CancellationToken ct)
    {
        _snapshot = null;

        // Hydrate-once: LoadWiredAsync runs on every live pile resolution (every fire), so this
        // method must stay cheap after the first call. The eager normalization below is only needed
        // when the config first comes out of JSON — afterwards ApplyWiredUpdateAsync validates
        // everything it writes, and stuff/variable ids self-heal on read (GetStuffIds /
        // GetValidVariableIds prune stale entries lazily). Leaf overrides still run on every call to
        // refresh their cached params, which is intentional and cheap.
        if (_wiredData is not null)
        {
            return Task.CompletedTask;
        }

        if (
            _ctx.RoomObject.ExtraData.TryGetSection(
                ExtraDataSectionType.WIRED,
                out JsonElement wiredDataElement
            )
        )
        {
            _wiredData = wiredDataElement.Deserialize<WiredData>() ?? new WiredData();
        }
        else
        {
            _wiredData = new WiredData();
        }

        _wiredData.AttatchRules(BuildIntParamRules(_wiredData.IntParams.Count));

        // Register persistence before the normalization below, so any repair it makes (pruned stale
        // ids, resized slots, rematerialized specifics) is written back instead of re-derived on
        // every future hydration.
        _wiredData.SetAction(() =>
        {
            _ctx.RoomObject.ExtraData.UpdateSection(
                ExtraDataSectionType.WIRED,
                JsonSerializer.SerializeToNode(_wiredData, _wiredData.GetType())
            );

            return Task.CompletedTask;
        });

        List<int> repairedIntParams = RepairIntParams(_wiredData.IntParams);

        if (!_wiredData.IntParams.SequenceEqual(repairedIntParams))
        {
            _wiredData.IntParams = repairedIntParams;
            _wiredData.MarkDirty();

            // The repair resizes the list, so the rules have to be resized with it — a box that
            // came back one param short and was padded would otherwise keep rules for the length
            // it had before.
            _wiredData.AttatchRules(BuildIntParamRules(_wiredData.IntParams.Count));
        }

        if (GetValidStuffIds(_wiredData.StuffIds, out List<int> stuffIds))
        {
            if (!_wiredData.StuffIds.SequenceEqual(stuffIds))
            {
                _wiredData.StuffIds = stuffIds;

                _wiredData.MarkDirty();
            }
        }

        if (GetValidStuffIds(_wiredData.StuffIds2, out List<int> stuffIds2))
        {
            if (!_wiredData.StuffIds2.SequenceEqual(stuffIds2))
            {
                _wiredData.StuffIds2 = stuffIds2;

                _wiredData.MarkDirty();
            }
        }

        if (GetValidVariableIds(_wiredData.VariableIds, out List<WiredVariableId> variableIds))
        {
            List<string> variableIdStrings = variableIds.Select(x => x.ToString()).ToList();

            if (!_wiredData.VariableIds.SequenceEqual(variableIdStrings))
            {
                _wiredData.VariableIds = variableIdStrings;

                _wiredData.MarkDirty();
            }
        }

        // Ensure the definition/type-specific slots exist and are correctly sized before any leaf
        // reads them in its own FillInternalDataAsync (conditions/selectors read the quantifier +
        // invert flags, actions read the delay). A freshly placed, never-configured box otherwise has
        // empty lists and GetDefinitionParam(0) throws IndexOutOfRange. GetDefinitionSpecifics /
        // GetTypeSpecifics preserve any valid persisted values and fill defaults for the rest.
        _wiredData.DefinitionSpecifics = GetDefinitionSpecifics();
        _wiredData.TypeSpecifics = GetTypeSpecifics();

        return Task.CompletedTask;
    }

    protected virtual WiredDataSnapshot BuildSnapshot()
    {
        return new WiredDataSnapshot
        {
            WiredType = WiredType,
            FurniLimit = _ctx.WiredLimits.WiredSelectedItemsLimit,
            StuffIds = GetValidStuffIds(_wiredData.StuffIds, out List<int> validStuffIds)
                ? validStuffIds
                : [],
            StuffIds2 = GetValidStuffIds(_wiredData.StuffIds2, out List<int> validStuffIds2)
                ? validStuffIds2
                : [],
            StuffTypeId = _ctx.Definition.SpriteId,
            Id = _ctx.ObjectId,
            StringParam = _wiredData.StringParam,
            IntParams = _wiredData.IntParams,
            VariableIds = GetValidVariableIds(
                _wiredData.VariableIds,
                out List<WiredVariableId> validVariableIds
            )
                ? validVariableIds
                : [],
            FurniSourceTypes = GetFurniSources(),
            PlayerSourceTypes = GetPlayerSources(),
            Code = WiredCode,
            AdvancedMode = SupportsAdvancedMode(),
            AmountFurniSelections = [],
            AllowWallFurni = _ctx.WiredLimits.WiredAllowWallFurni,
            AllowedFurniSources = GetAllowedFurniSources(),
            AllowedPlayerSources = GetAllowedPlayerSources(),
            DefaultFurniSources = GetDefaultFurniSources(),
            DefaultPlayerSources = GetDefaultPlayerSources(),
            DefinitionSpecifics = GetDefinitionSpecifics(),
            TypeSpecifics = GetTypeSpecifics(),
            ContextSnapshots = GetWiredContextSnapshots(),
            DefaultIntParams = GetDefaultIntParams(),
        };
    }

    public override async Task OnAttachAsync(CancellationToken ct)
    {
        await base.OnAttachAsync(ct);

        await OnWiredStackChangedAsync(
            ActionContext.CreateForSystem(_ctx.RoomId),
            [_ctx.GetTileIdx()],
            ct
        );
    }

    public override async Task OnDetachAsync(CancellationToken ct)
    {
        await base.OnDetachAsync(ct);

        await OnWiredStackChangedAsync(
            ActionContext.CreateForSystem(_ctx.RoomId),
            [_ctx.GetTileIdx()],
            ct
        );
    }

    public override Task OnStateChangedAsync(CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    public override async Task OnMoveAsync(ActionContext ctx, int prevIdx, CancellationToken ct)
    {
        await base.OnMoveAsync(ctx, prevIdx, ct);

        await OnWiredStackChangedAsync(ctx, [_ctx.GetTileIdx(), prevIdx], ct);
    }

    public override async Task OnPickupAsync(ActionContext ctx, CancellationToken ct)
    {
        await base.OnPickupAsync(ctx, ct);

        _ctx.RoomObject.ExtraData.DeleteSection(ExtraDataSectionType.WIRED);

        await OnWiredStackChangedAsync(ctx, [_ctx.GetTileIdx()], ct);
    }

    public override Task OnUseAsync(ActionContext ctx, int param, CancellationToken ct)
    {
        _ = _grainFactory
            .GetPlayerPresenceGrain(ctx.PlayerId)
            .SendComposerAsync(new OpenEventMessageComposer { ItemId = _ctx.ObjectId })
            .ConfigureAwait(false);

        return Task.CompletedTask;
    }

    protected virtual Task OnWiredStackChangedAsync(
        ActionContext ctx,
        List<int> ids,
        CancellationToken ct
    )
    {
        return _ctx.PublishRoomEventAsync(
            new RoomWiredStackChangedEvent
            {
                RoomId = _ctx.RoomId,
                CausedBy = ctx,
                StackIds = ids,
            },
            ct
        );
    }
}
