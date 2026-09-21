using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums.Wired;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Primitives.Rooms.Object.Logic;
using Vortex.Primitives.Rooms.Wired;
using Vortex.Rooms.Wired;

namespace Vortex.Rooms.Object.Logic.Furniture.Floor.Wired.Conditions;

/// <summary>
/// "The user is a member of the group". The client's form is a radio group — "Current group" (which
/// sends an empty string param) or a guild picked from the configuring player's own guilds (which
/// sends its id as a decimal string) — so <see cref="WiredGroupTarget"/> owns that reading. The
/// negative variant inherits this and flips <see cref="FurnitureWiredConditionLogic.IsNegative"/>.
/// </summary>
[RoomObjectLogic("wf_cnd_actor_in_group")]
// The same box under the name a custom furni pack in this hotel gives it
// (`bobbacrew_user_is_in_group`). A logic binds by the `logic` column and by nothing else, so a
// second furni for a box we already have needs its key here or it is placeable and inert: this one
// was. The behaviour and the wired code are this class's, so there is nothing else to write.
[RoomObjectLogic("wf_cnd_habbo_in_group")]
public class WiredConditionGroupMember(
    IGrainFactory grainFactory,
    IStuffDataFactory stuffDataFactory,
    IRoomFloorItemContext ctx
) : FurnitureWiredConditionLogic(grainFactory, stuffDataFactory, ctx)
{
    public override int WiredCode => (int)WiredConditionType.ACTOR_IS_GROUP_MEMBER;

    public override async Task PrepareAsync(IWiredProcessingContext ctx, CancellationToken ct)
    {
        if (WiredGroupTarget.Resolve(_wiredData.StringParam, _ctx.GroupId) is int groupId)
        {
            await _ctx.Furni.EnsureGuildRosterAsync(groupId, ct);
        }
    }

    public override bool Evaluate(IWiredProcessingContext ctx)
    {
        PlayerId triggerer = ctx.Event.CausedBy.PlayerId;
        bool result = false;

        // An unresolvable target (the box left on "current group" in a room with no guild) fails
        // rather than matching everyone; the negative variant then passes, which is the same reading
        // the client gives a box whose group no longer exists.
        if (
            triggerer > 0
            && WiredGroupTarget.Resolve(_wiredData.StringParam, _ctx.GroupId) is int groupId
        )
        {
            result = _ctx.Furni.IsGuildMember(groupId, triggerer);
        }

        return IsNegative() ? !result : result;
    }
}
