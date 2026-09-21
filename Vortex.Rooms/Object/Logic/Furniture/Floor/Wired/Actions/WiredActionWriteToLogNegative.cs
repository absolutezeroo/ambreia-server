using Orleans;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Rooms.Enums.Wired;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Primitives.Rooms.Object.Logic;

namespace Vortex.Rooms.Object.Logic.Furniture.Floor.Wired.Actions;

/// <summary>
/// The same line, on the other branch: it writes when the pile's trigger fired but its conditions
/// did not hold.
/// </summary>
/// <remarks>
/// The client declares both halves on one class — <c>code</c> 49 and <c>negativeCode</c> 50 — and
/// the engine here binds a branch per logic, so the negative half is its own box. It is the more
/// useful of the two while debugging: "the trigger fired and the conditions refused" is the state a
/// builder cannot otherwise observe at all.
/// </remarks>
[RoomObjectLogic("wf_act_neg_write_to_logs")]
// `wf_act_neg_log`, which is the name this hotel's furni carries; see the positive half.
[RoomObjectLogic("wf_act_neg_log")]
public class WiredActionWriteToLogNegative(
    IGrainFactory grainFactory,
    IStuffDataFactory stuffDataFactory,
    IRoomFloorItemContext ctx
) : WiredActionWriteToLog(grainFactory, stuffDataFactory, ctx)
{
    public override int WiredCode => (int)WiredActionType.NEG_WRITE_TO_LOG;

    public override bool IsNegative() => true;
}
