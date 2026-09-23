using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Furniture.Providers;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Primitives.Rooms.Wired;
using Vortex.Rooms.Object.Logic.Furniture.Floor.Wired.Addons;
using Vortex.Rooms.Wired;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Wired;

/// <summary>
/// "Animation Curve" (addon 22): the strength reaches the pile's policy, the policy reaches every
/// furni move, and a projectile with a curve of its own keeps it.
/// </summary>
public sealed class WiredMovementCurveTests
{
    [Fact]
    public async Task TheStrengthBecomesThePilesCurve()
    {
        WiredPolicy policy = new();

        await Curve(strength: 150).MutatePolicyAsync(Processing(policy), CancellationToken.None);

        policy.CurveStrength.Should().Be(150);
    }

    [Fact]
    public async Task EveryFurniTheCurvedPileMovesArcs()
    {
        FakeWiredRoomHost room = new();
        WiredExecutionContext ctx = new(room) { Policy = new WiredPolicy { CurveStrength = 150 } };

        await ctx.ProcessFloorItemMovementAsync(Item(10), 22, null, null);

        ctx.FloorItemMoves.Single().CurveStrength.Should().Be(150);
    }

    [Fact]
    public async Task AFlatPileSendsNoCurveAtAll()
    {
        WiredExecutionContext ctx = new(new FakeWiredRoomHost()) { Policy = new WiredPolicy() };

        await ctx.ProcessFloorItemMovementAsync(Item(10), 22, null, null);

        ctx.FloorItemMoves.Single().CurveStrength.Should().BeNull();
    }

    private static WiredAddonMovementCurve Curve(int strength)
    {
        WiredData data = new() { IntParams = [0, strength, 0] };
        TestCurve addon = new(WiredTestBoxes.Context(), data);

        data.AttatchRules(addon.GetIntParamRules());

        return addon;
    }

    private static IWiredProcessingContext Processing(IWiredPolicy policy) =>
        FakeProxy.Create<IWiredProcessingContext>(call =>
            call.Method.Name switch
            {
                "get_Policy" => policy,
                "get_Selected" => new WiredSelectionSet(),
                _ => null,
            }
        );

    private static IRoomFloorItem Item(int objectId)
    {
        RoomObjectId id = new(objectId);

        return FakeProxy.Create<IRoomFloorItem>(call =>
            call.Method.Name switch
            {
                "get_ObjectId" => id,
                "get_Z" => Altitude.Zero,
                "get_Rotation" => Rotation.North,
                _ => null,
            }
        );
    }

    private sealed class TestCurve : WiredAddonMovementCurve
    {
        public TestCurve(IRoomFloorItemContext ctx, WiredData data)
            : base(null!, new StuffDataFactory(), ctx) => _wiredData = data;
    }
}
