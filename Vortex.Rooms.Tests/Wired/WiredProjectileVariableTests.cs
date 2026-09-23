using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Furniture.Providers;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Enums.Wired;
using Vortex.Primitives.Rooms.Events;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Primitives.Rooms.Snapshots.Wired;
using Vortex.Primitives.Rooms.Snapshots.Wired.Variables;
using Vortex.Primitives.Rooms.Wired;
using Vortex.Primitives.Rooms.Wired.Variable;
using Vortex.Rooms.Object.Logic.Furniture.Floor.Wired.Addons;
using Vortex.Rooms.Wired;
using Vortex.Rooms.Wired.Projectile;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Wired;

/// <summary>
/// The projectile's internal variables: a shaped move becomes a flight the readings interpolate,
/// the interceptable ones announce each change once, and collisions count only the real path.
/// </summary>
public sealed class WiredProjectileVariableTests
{
    private const int Shot = 10;

    private const int AllReadings = 0b111_1111;

    [Fact]
    public void AFlightIsReadAsTheClientDrawsIt()
    {
        // 4 real tiles east in 400 ms, drawn 6 tiles long over 600 ms.
        ProjectileFlight flight = new(0, 600, 0, 0, 0, 6, 0, 0, 6, 4, 0);

        flight.TilesTravelled(300).Should().Be(3);
        flight.X(300).Should().Be(3);
        flight.IsTravelling(300).Should().BeTrue();

        // Past the target the count goes on, but the real path stops at 4.
        flight.TilesTravelled(600).Should().Be(6);
        flight.PathStepsCrossed(600).Should().Be(4);
        flight.IsTravelling(600).Should().BeFalse();
    }

    [Fact]
    public void TheReadingsHangOffTheFurniVariableTheyAreStackedWith()
    {
        WiredAddonProjectile addon = Addon(new Room());

        addon
            .CreateSubVariables(Parent("@shot", WiredVariableTargetType.Furni))
            .Select(v => v.GetVarSnapshot().VariableName)
            .Should()
            .Equal(
                "@shot.animation.tiles_travelled",
                "@shot.animation.user_collisions",
                "@shot.animation.furni_collisions",
                "@shot.animation.position.x",
                "@shot.animation.position.y",
                "@shot.animation.position.altitude",
                "@shot.animation.is_travelling"
            );

        addon.CreateSubVariables(Parent("@who", WiredVariableTargetType.User)).Should().BeEmpty();
    }

    [Fact]
    public async Task AnInterceptableReadingIsAnnouncedOncePerChange()
    {
        Room room = new();
        WiredAddonProjectile addon = Addon(room);
        IWiredPolledVariable tiles = (IWiredPolledVariable)
            addon.CreateSubVariables(Parent("@shot", WiredVariableTargetType.Furni))[0];

        await ShootAsync(addon, toX: 4);

        tiles.Poll(200).Select(e => (e.Previous, e.Current)).Should().Equal((0, 2));
        tiles.Poll(200).Should().BeEmpty();
        tiles.Poll(400).Select(e => (e.Previous, e.Current)).Should().Equal((2, 4));
    }

    [Fact]
    public async Task CollisionsCountWhatStandsOnTheCrossedPath_NotTheShooterOrTheShot()
    {
        Room room = new();
        room.Avatars[(0, 0)] = [new RoomObjectId(1)]; // the shooter, on the source tile
        room.Avatars[(2, 0)] = [new RoomObjectId(2)];
        room.Avatars[(4, 0)] = [new RoomObjectId(3)];
        room.Furni[(4, 0)] = [new RoomObjectId(Shot)]; // the shot, landed on its target

        WiredAddonProjectile addon = Addon(room);

        await ShootAsync(addon, toX: 4);

        addon.TryRead(1, Shot, 200, out int halfway).Should().BeTrue();
        halfway.Should().Be(1);
        addon.TryRead(1, Shot, 400, out int landed).Should().BeTrue();
        landed.Should().Be(2);
        addon.TryRead(2, Shot, 400, out int furni).Should().BeTrue();
        furni.Should().Be(0);
        addon.TryRead(0, 99, 400, out _).Should().BeFalse();
    }

    private static Task<WiredFloorItemMovementSnapshot> ShootAsync(
        WiredAddonProjectile addon,
        int toX
    ) =>
        addon.ShapeFloorItemMoveAsync(
            FakeProxy.Create<IRoomFloorItem>(call =>
                call.Method.Name == "get_ObjectId" ? new RoomObjectId(Shot) : null
            ),
            new WiredFloorItemMovementSnapshot
            {
                ObjectId = new RoomObjectId(Shot),
                SourceX = 0,
                SourceY = 0,
                SourceZ = Altitude.Zero,
                TargetX = toX,
                TargetY = 0,
                TargetZ = Altitude.Zero,
                Rotation = Rotation.North,
                AnimationTime = toX * 100,
            },
            FakeProxy.Create<IWiredExecutionContext>(call =>
                call.Method.Name == "GetEffectiveSelectionAsync"
                    ? Task.FromResult<IWiredSelectionSet>(new WiredSelectionSet())
                    : null
            ),
            CancellationToken.None
        );

    private static WiredAddonProjectile Addon(Room room)
    {
        List<int> ints = [.. Enumerable.Repeat(0, 19)];
        ints[11] = AllReadings;

        WiredData data = new() { IntParams = ints };
        TestProjectile addon = new(WiredTestBoxes.Context(objectId: 50, map: room.Map()), data);

        data.AttatchRules(addon.GetIntParamRules());

        return addon;
    }

    private static IWiredVariable Parent(string name, WiredVariableTargetType target)
    {
        WiredVariableSnapshot snapshot = new()
        {
            VariableId = new WiredVariableId(1),
            VariableName = name,
            VariableType = WiredVariableType.Created,
            VariableHash = new WiredVariableHash(0),
            AvailabilityType = WiredAvailabilityType.RoomActive,
            TargetType = target,
            Flags = WiredVariableFlags.HasValue,
            TextConnectors = [],
        };

        return FakeProxy.Create<IWiredVariable>(call =>
            call.Method.Name == nameof(IWiredVariable.GetVarSnapshot) ? snapshot : null
        );
    }

    /// <summary>A strip of floor ten tiles wide, holding whoever the test puts on it.</summary>
    private sealed class Room
    {
        public Dictionary<(int X, int Y), HashSet<RoomObjectId>> Avatars { get; } = [];

        public Dictionary<(int X, int Y), HashSet<RoomObjectId>> Furni { get; } = [];

        public IRoomMapAccess Map() =>
            FakeProxy.Create<IRoomMapAccess>(call =>
                call.Method.Name switch
                {
                    "ToIdx" => ((int)call.Args![1]! * 10) + (int)call.Args[0]!,
                    "AvatarStackAt" => At(Avatars, (int)call.Args![0]!),
                    "FloorStackAt" => At(Furni, (int)call.Args![0]!),
                    _ => null,
                }
            );

        private static IReadOnlySet<RoomObjectId> At(
            Dictionary<(int X, int Y), HashSet<RoomObjectId>> tiles,
            int idx
        ) =>
            tiles.TryGetValue((idx % 10, idx / 10), out HashSet<RoomObjectId>? here)
                ? here
                : new HashSet<RoomObjectId>();
    }

    private sealed class TestProjectile : WiredAddonProjectile
    {
        public TestProjectile(IRoomFloorItemContext ctx, WiredData data)
            : base(null!, new StuffDataFactory(), ctx) => _wiredData = data;
    }
}
