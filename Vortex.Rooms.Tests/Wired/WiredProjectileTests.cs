using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Furniture.Providers;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Primitives.Rooms.Snapshots.Wired;
using Vortex.Primitives.Rooms.Wired;
using Vortex.Rooms.Object.Logic.Furniture.Floor.Wired.Addons;
using Vortex.Rooms.Wired;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Wired;

/// <summary>
/// The projectile add-on. Its behaviour is derived rather than recovered (see the class), so these
/// pin the derivation: the four direction systems as the dialog's pictures draw them, the units the
/// client's slide expects, and that the add-on is reached at all from inside a move.
/// </summary>
public sealed class WiredProjectileTests
{
    private const int Projectile = 10;

    [Theory]
    // 0, straight: only an exact axis is straight, anything off it is a diagonal.
    [InlineData(0, 5, 0, Rotation.East)]
    [InlineData(0, 5, 1, Rotation.SouthEast)]
    [InlineData(0, -2, -7, Rotation.NorthWest)]
    // 1, diffuse: each direction owns the 45° around it.
    [InlineData(1, 5, 1, Rotation.East)]
    [InlineData(1, 5, 4, Rotation.SouthEast)]
    [InlineData(1, -1, -6, Rotation.North)]
    // 2 and 3, four directions: the longer axis, and they part only on the exact diagonal.
    [InlineData(2, 3, -2, Rotation.East)]
    [InlineData(2, 3, 3, Rotation.East)]
    [InlineData(3, 3, 3, Rotation.South)]
    [InlineData(3, -1, -4, Rotation.North)]
    public void EachSystemPointsWhereItsPictureSays(
        int system,
        int dx,
        int dy,
        Rotation expected
    ) => WiredAddonProjectile.Direction(system, dx, dy).Should().Be(expected);

    [Fact]
    public void AMoveThatGoesNowhereHasNoDirection() =>
        WiredAddonProjectile.Direction(1, 0, 0).Should().BeNull();

    [Fact]
    public void FlightTimeAccelerates_ButNoTileIsFasterThanAMillisecond()
    {
        WiredAddonProjectile.ScaledTime(3, 100, 0, 500).Should().Be(300);
        WiredAddonProjectile.ScaledTime(3, 100, 30, 500).Should().Be(100 + 70 + 40);
        WiredAddonProjectile.ScaledTime(4, 100, 60, 500).Should().Be(100 + 40 + 1 + 1);
        WiredAddonProjectile.ScaledTime(0, 100, 0, 500).Should().Be(500);
    }

    [Fact]
    public void OvershootIsTimeAtTheSameSpeed()
    {
        // 4 tiles in 400 ms: 2 more tiles is 200 ms more.
        WiredAddonProjectile.OvershootMs(mode: 1, tiles: 2, steps: 4, time: 400).Should().Be(200);

        // "Always shoot 6" over a 4-tile move is the same 2 tiles; "always 2" stops short.
        WiredAddonProjectile.OvershootMs(mode: 2, tiles: 6, steps: 4, time: 400).Should().Be(200);
        WiredAddonProjectile.OvershootMs(mode: 2, tiles: 2, steps: 4, time: 400).Should().Be(-200);

        // The slide never shrinks to nothing, and "normal" sends none.
        WiredAddonProjectile
            .OvershootMs(mode: 2, tiles: -64, steps: 1, time: 400)
            .Should()
            .Be(-399);
        WiredAddonProjectile.OvershootMs(mode: 0, tiles: 5, steps: 4, time: 400).Should().BeNull();
        WiredAddonProjectile.OvershootMs(mode: 1, tiles: 5, steps: 0, time: 400).Should().BeNull();
    }

    [Fact]
    public async Task AShapedMoveCarriesTimeOvershootAndArc()
    {
        WiredAddonProjectile addon = Addon(
            scale: true,
            perTile: 100,
            distanceMode: 1,
            tiles: 2,
            curve: 250
        );

        WiredFloorItemMovementSnapshot shaped = await addon.ShapeFloorItemMoveAsync(
            Item(Projectile, 0, 0),
            Move(Projectile, 0, 0, 4, 0, time: 500),
            Execution(),
            CancellationToken.None
        );

        shaped.AnimationTime.Should().Be(400);
        shaped.OvershootingDistance.Should().Be(200);
        shaped.CurveStrength.Should().Be(250);
    }

    [Fact]
    public async Task AFurniThatIsNotTheProjectileIsLeftAlone()
    {
        WiredAddonProjectile addon = Addon(scale: true, perTile: 100, curve: 250);
        WiredFloorItemMovementSnapshot move = Move(11, 0, 0, 4, 0, time: 500);

        WiredFloorItemMovementSnapshot shaped = await addon.ShapeFloorItemMoveAsync(
            Item(11, 0, 0),
            move,
            Execution(selectedFurni: Projectile),
            CancellationToken.None
        );

        shaped.Should().Be(move);
    }

    [Fact]
    public async Task TheProjectileFacesWhereItFlies_PlusTheOffset()
    {
        WiredAddonProjectile addon = Addon(rotate: true, system: 0, offset: 1);

        Rotation? rotation = await addon.RotateFloorItemMoveAsync(
            Item(Projectile, 0, 0),
            3,
            3,
            Execution(),
            CancellationToken.None
        );

        rotation.Should().Be(Rotation.South);
    }

    [Fact]
    public async Task AMoveMadeInThePile_IsShapedBeforeItIsSent()
    {
        FakeWiredRoomHost room = new();
        WiredExecutionContext ctx = new(room)
        {
            Addons = [Addon(curve: 300)],
            Policy = new WiredPolicy(),
        };

        await ctx.ProcessFloorItemMovementAsync(Item(Projectile, 1, 1), 22, null, null);

        room.FloorItemMoves.Should().ContainSingle();
        ctx.FloorItemMoves.Single().CurveStrength.Should().Be(300);
    }

    private static WiredAddonProjectile Addon(
        bool rotate = false,
        int system = 0,
        bool scale = false,
        int perTile = 100,
        int offset = 0,
        int distanceMode = 0,
        int tiles = 0,
        int curve = 0
    )
    {
        WiredData data = new()
        {
            IntParams =
            [
                rotate ? 1 : 0,
                system,
                scale ? 1 : 0,
                0,
                perTile,
                0,
                1,
                1,
                0,
                0,
                offset,
                0,
                0,
                0,
                distanceMode,
                0,
                tiles,
                0,
                curve,
            ],
        };

        TestProjectile addon = new(WiredTestBoxes.Context(), data);

        data.AttatchRules(addon.GetIntParamRules());

        return addon;
    }

    private static IRoomFloorItem Item(int objectId, int x, int y)
    {
        RoomObjectId id = new(objectId);

        return FakeProxy.Create<IRoomFloorItem>(call =>
            call.Method.Name switch
            {
                "get_ObjectId" => id,
                "get_X" => x,
                "get_Y" => y,
                "get_Z" => Altitude.Zero,
                "get_Rotation" => Rotation.North,
                _ => null,
            }
        );
    }

    private static WiredFloorItemMovementSnapshot Move(
        int objectId,
        int fromX,
        int fromY,
        int toX,
        int toY,
        int time
    ) =>
        new()
        {
            ObjectId = new RoomObjectId(objectId),
            SourceX = fromX,
            SourceY = fromY,
            SourceZ = Altitude.Zero,
            TargetX = toX,
            TargetY = toY,
            TargetZ = Altitude.Zero,
            Rotation = Rotation.North,
            AnimationTime = time,
        };

    /// <summary>A pile whose projectile slot resolves to <paramref name="selectedFurni"/>, or to
    /// nothing — which makes every moved furni the projectile.</summary>
    private static IWiredExecutionContext Execution(int? selectedFurni = null)
    {
        WiredSelectionSet selection = new();

        if (selectedFurni is int id)
        {
            selection.SelectedFurniIds.Add(id);
        }

        return FakeProxy.Create<IWiredExecutionContext>(call =>
            call.Method.Name == "GetEffectiveSelectionAsync"
                ? Task.FromResult<IWiredSelectionSet>(selection)
                : null
        );
    }

    private sealed class TestProjectile : WiredAddonProjectile
    {
        public TestProjectile(IRoomFloorItemContext ctx, WiredData data)
            : base(null!, new StuffDataFactory(), ctx) => _wiredData = data;
    }
}
