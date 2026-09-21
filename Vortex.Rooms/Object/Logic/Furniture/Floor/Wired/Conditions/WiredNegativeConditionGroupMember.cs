using Orleans;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Rooms.Enums.Wired;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Primitives.Rooms.Object.Logic;

namespace Vortex.Rooms.Object.Logic.Furniture.Floor.Wired.Conditions;

[RoomObjectLogic("wf_cnd_not_actor_in_group")]
[RoomObjectLogic("wf_cnd_not_in_group")]
// `bobbacrew_user_not_in_group`, the custom pack's copy of this box; see the positive twin.
[RoomObjectLogic("wf_cnd_not_habbo_in_group")]
public class WiredNegativeConditionGroupMember(
    IGrainFactory grainFactory,
    IStuffDataFactory stuffDataFactory,
    IRoomFloorItemContext ctx
) : WiredConditionGroupMember(grainFactory, stuffDataFactory, ctx)
{
    public override int WiredCode => (int)WiredConditionType.NOT_ACTOR_IS_GROUP_MEMBER;

    public override bool IsNegative() => true;
}
