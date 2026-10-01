using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Rooms.Enums.Wired;
using Vortex.Primitives.Rooms.Object.Furniture;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Primitives.Rooms.Object.Logic;
using Vortex.Primitives.Rooms.Snapshots.Wired.Variables;
using Vortex.Primitives.Rooms.Wired;
using Vortex.Primitives.Rooms.Wired.Variable;
using Vortex.Rooms.Wired;
using Vortex.Rooms.Wired.Rules;

namespace Vortex.Rooms.Object.Logic.Furniture.Floor.Wired.Selectors;

/// <summary>
/// Picks every furni in the room holding the chosen variable, optionally only those whose value
/// passes a comparison.
/// </summary>
/// <remarks>
/// Five int params, as the client's <c>VariableSelector.readIntParamsFromForm</c> pushes them: [0]
/// the comparison (the radio button's own id), [1] 0 for "holding it at all", 1 for "compare to a
/// number" and 2 for "compare to another variable" (the checkbox, then the value-or-variable
/// option plus one), [2] and [3] the number as a <c>pushIntAsLong</c> pair, [4] the reference
/// variable's source type. Variable ids: the subject, then the reference. The reference is read
/// against the box's one merged input source (<c>mergedSelections() = [[0, 0]]</c>).
/// </remarks>
[RoomObjectLogic("wf_slc_furni_with_var")]
public class WiredSelectorItemsWithVariable(
    IGrainFactory grainFactory,
    IStuffDataFactory stuffDataFactory,
    IRoomFloorItemContext ctx
) : FurnitureWiredSelectorLogic(grainFactory, stuffDataFactory, ctx)
{
    private const int HoldingOnly = 0;
    private const int AgainstNumber = 1;

    public override int WiredCode => (int)WiredSelectorType.FURNI_WITH_VARIABLE;

    /// <summary>The kind of target the subject variable lives on, and the selection it fills.</summary>
    protected virtual WiredVariableTargetType SubjectTarget => WiredVariableTargetType.Furni;

    public override int GetMaxVariableIds() => 2;

    public override List<IWiredParamRule> GetIntParamRules() =>
        [
            new WiredEnumParamRule<WiredComparisonType>(WiredComparisonType.GreaterThan),
            new WiredRangeParamRule(0, 2, 0),
            new WiredParamRule(0),
            new WiredParamRule(0), // set value
            new WiredEnumParamRule<WiredVariableTargetType>(WiredVariableTargetType.Furni),
        ];

    public override List<WiredFurniSourceType[]> GetAllowedFurniSources() =>
        [
            [
                WiredFurniSourceType.TriggeredItem,
                WiredFurniSourceType.SelectedItems,
                WiredFurniSourceType.SignalItems,
            ],
        ];

    public override List<WiredPlayerSourceType[]> GetAllowedPlayerSources() =>
        [
            [WiredPlayerSourceType.TriggeredUser, WiredPlayerSourceType.SignalUsers],
        ];

    public override List<WiredVariableContextSnapshot> GetWiredContextSnapshots() =>
        [
            new WiredVariableAllInRoomSnapshot()
            {
                ContextType = WiredContextType.AllVariablesInRoom,
                AllVariablesHash = _ctx.Furni.AllVariablesHash,
            },
        ];

    public override async Task<IWiredSelectionSet> SelectAsync(
        IWiredProcessingContext ctx,
        CancellationToken ct
    )
    {
        WiredSelectionSet output = new WiredSelectionSet();

        if (
            _wiredData.VariableIds.Count == 0
            || !WiredVariableAccess.TryResolve(
                _ctx.Furni,
                _wiredData.VariableIds[0],
                out WiredVariableId subjectId,
                out IWiredVariable? subject
            )
        )
        {
            return output;
        }

        int mode = _wiredData.IntParams.Count > 1 ? _wiredData.GetIntParam<int>(1) : HoldingOnly;
        int operand = 0;

        if (mode != HoldingOnly)
        {
            if (_wiredData.IntParams.Count < 5)
            {
                return output;
            }

            if (mode == AgainstNumber)
            {
                operand = WiredIntAsLong.ReadClamped(
                    _wiredData.GetIntParam<int>(2),
                    _wiredData.GetIntParam<int>(3)
                );
            }
            else
            {
                IWiredSelectionSet input = await ctx.GetWiredSelectionSetAsync(this, ct);

                // A reference that holds nothing selects nothing, rather than comparing against a
                // stand-in zero -- the same rule as the variable-value condition.
                if (
                    !WiredVariableAccess.TryRead(
                        _ctx.Furni,
                        _wiredData.VariableIds.Count > 1 ? _wiredData.VariableIds[1] : string.Empty,
                        _wiredData.GetIntParam<WiredVariableTargetType>(4),
                        input,
                        out WiredVariableValue reference
                    )
                )
                {
                    return output;
                }

                operand = reference.Value;
            }
        }

        WiredComparisonType comparison =
            _wiredData.IntParams.Count > 0
                ? _wiredData.GetIntParam<WiredComparisonType>(0)
                : WiredComparisonType.GreaterThan;

        foreach (int targetId in CandidateIds())
        {
            if (
                !subject!.TryGetValue(
                    new WiredVariableKey(subjectId, SubjectTarget, targetId),
                    out WiredVariableValue value
                )
            )
            {
                continue;
            }

            if (
                mode != HoldingOnly
                && !WiredVariableComparison.Matches(value.Value, comparison, operand)
            )
            {
                continue;
            }

            Add(output, targetId);
        }

        return output;
    }

    /// <summary>Everything in the room the subject variable could be keyed by.</summary>
    protected virtual IEnumerable<int> CandidateIds()
    {
        foreach (IRoomItem item in _ctx.Lookup.Items)
        {
            yield return (int)item.ObjectId;
        }
    }

    protected virtual void Add(WiredSelectionSet output, int targetId) =>
        output.SelectedFurniIds.Add(targetId);
}
