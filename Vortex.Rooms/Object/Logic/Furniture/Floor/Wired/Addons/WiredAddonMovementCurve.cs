using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Rooms.Enums.Wired;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Primitives.Rooms.Object.Logic;
using Vortex.Primitives.Rooms.Wired;
using Vortex.Primitives.Rooms.Wired.Variable;
using Vortex.Rooms.Wired;
using Vortex.Rooms.Wired.Rules;

namespace Vortex.Rooms.Object.Logic.Furniture.Floor.Wired.Addons;

/// <summary>
/// "WIRED Add-on: Animation Curve" — the pile's moves arc instead of sliding flat: furni by their
/// <c>curveStrength</c>, users by their <c>jumpPower</c>, both from one strength (-1000..1000).
/// </summary>
/// <remarks>
/// The client calls the box <c>AddonCodes.JUMP_STRENGTH</c> (22) and labels its one field "Movement
/// curve"; the projectile add-on's straight trajectory says it "can be overridden with WIRED Add-on:
/// Animation Curve", which is this box — a projectile set to its own curve keeps it, because it
/// shapes its move after the pile's policy has. The strength is sent as is: the client lifts a
/// slide's midpoint by strength / 100 of a quarter of its length, so 100 is a gentle hop and a
/// negative strength dips. Bound to <c>wf_xtra_mov_curve</c>, the name the catalogue gives it.
/// </remarks>
[RoomObjectLogic("wf_xtra_mov_curve")]
public class WiredAddonMovementCurve(
    IGrainFactory grainFactory,
    IStuffDataFactory stuffDataFactory,
    IRoomFloorItemContext ctx
) : FurnitureWiredAddonLogic(grainFactory, stuffDataFactory, ctx)
{
    public override int WiredCode => (int)WiredAddonType.JUMP_STRENGTH;

    /// <summary>A value-or-variable section's radio: 0 is the typed number, 1 a variable.</summary>
    private const int FromVariable = 1;

    public override List<IWiredParamRule> GetIntParamRules() =>
        [
            new WiredRangeParamRule(0, 1, 0), // 0 number or variable
            new WiredRangeParamRule(-1000, 1000, 80), // 1 strength
            new WiredParamRule(0), // 2 the variable's target
        ];

    public override int GetMaxVariableIds() => 1;

    public override Task<bool> MutatePolicyAsync(IWiredProcessingContext ctx, CancellationToken ct)
    {
        ctx.Policy.CurveStrength = Strength(ctx.Selected);

        return Task.FromResult(true);
    }

    /// <summary>The typed strength, or the variable's when the box reads one. A variable that holds
    /// nothing falls back to the typed number rather than flattening the move.</summary>
    private int Strength(IWiredSelectionSet selection)
    {
        int typed = Param(1);

        if (
            Param(0) == FromVariable
            && _wiredData.VariableIds.Count > 0
            && WiredVariableAccess.TryRead(
                _ctx.Furni,
                _wiredData.VariableIds[0],
                (WiredVariableTargetType)Param(2),
                selection,
                out WiredVariableValue read
            )
        )
        {
            return System.Math.Clamp(read.Value, -1000, 1000);
        }

        return typed;
    }

    private int Param(int index) =>
        index < _wiredData.IntParams.Count ? _wiredData.IntParams[index] : 0;
}
