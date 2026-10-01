using System.Collections.Generic;
using Orleans;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Rooms.Enums.Wired;
using Vortex.Primitives.Rooms.Object.Avatars;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Primitives.Rooms.Object.Logic;
using Vortex.Primitives.Rooms.Wired;
using Vortex.Rooms.Wired;

namespace Vortex.Rooms.Object.Logic.Furniture.Floor.Wired.Selectors;

/// <summary>
/// The users twin of <see cref="WiredSelectorItemsWithVariable"/>: every player in the room holding
/// the chosen variable, optionally filtered by its value. User variables are keyed by player id.
/// </summary>
/// <remarks>
/// Both client classes extend <c>VariableSelector</c> and neither overrides
/// <c>readIntParamsFromForm</c>, so the five int params and two variable ids are the furni twin's.
/// One rule was once declared here against the five the form sends, and TryNormalizeIntParams
/// refuses a count it has no rule for -- so this selector could never be saved at all, silently.
/// </remarks>
[RoomObjectLogic("wf_slc_users_with_var")]
public class WiredSelectorEntitiesWithVariable(
    IGrainFactory grainFactory,
    IStuffDataFactory stuffDataFactory,
    IRoomFloorItemContext ctx
) : WiredSelectorItemsWithVariable(grainFactory, stuffDataFactory, ctx)
{
    public override int WiredCode => (int)WiredSelectorType.USERS_WITH_VARIABLE;

    protected override WiredVariableTargetType SubjectTarget => WiredVariableTargetType.User;

    /// <summary>
    /// The pool this selector reads is one merged input source, and the client addresses it as
    /// slot 0 of both lists (<c>mergedSelections()</c> returns <c>[[0, 0]]</c>). Only the furni
    /// half was declared, so the arrow on a selector whose whole subject is users read past the
    /// end of the player list -- unchecked on both sides of the port.
    /// </summary>
    public override List<WiredFurniSourceType[]> GetAllowedFurniSources() =>
        [
            [
                WiredFurniSourceType.SelectedItems,
                WiredFurniSourceType.SignalItems,
                WiredFurniSourceType.TriggeredItem,
            ],
        ];

    /// <inheritdoc cref="GetAllowedFurniSources"/>
    public override List<WiredPlayerSourceType[]> GetAllowedPlayerSources() =>
        [
            [
                WiredPlayerSourceType.TriggeredUser,
                WiredPlayerSourceType.SelectorUsers,
                WiredPlayerSourceType.SignalUsers,
            ],
        ];

    protected override IEnumerable<int> CandidateIds()
    {
        foreach (IRoomAvatar avatar in _ctx.Lookup.Avatars)
        {
            if (avatar is IRoomPlayer player)
            {
                yield return (int)player.PlayerId;
            }
        }
    }

    protected override void Add(WiredSelectionSet output, int targetId) =>
        output.SelectedPlayerIds.Add(targetId);
}
