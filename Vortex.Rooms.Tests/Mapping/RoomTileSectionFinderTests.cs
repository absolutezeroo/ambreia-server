using FluentAssertions;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Mapping;
using Vortex.Primitives.Rooms.Object;
using Xunit;

namespace Vortex.Rooms.Tests.Mapping;

/// <summary>
/// The gap rule that decides whether an avatar fits under something.
///
/// These run on plain numbers rather than a room: <see cref="RoomTileSectionFinder" /> was written
/// as a pure function precisely so the one piece of arithmetic the whole 3D walk rests on can be
/// pinned down without a cluster, a grain or a furniture definition anywhere near it.
///
/// Everything below is a tile whose model floor is at 0. An item occupies <c>[Bottom, Top]</c> —
/// <c>Z</c> and <c>Z + stack height</c> — and <see cref="RoomTileSectionFinder.Clearance" /> is 2.
/// </summary>
public sealed class RoomTileSectionFinderTests
{
    private static readonly Altitude Floor = Altitude.Zero;
    private static readonly Altitude AnyStep = Altitude.FromValue(1000);

    private static RoomTileOccupant Item(
        int id,
        double bottom,
        double top,
        RoomTileFlags flags = RoomTileFlags.None
    ) =>
        new()
        {
            ItemId = id,
            Bottom = Altitude.FromValue(bottom),
            Top = Altitude.FromValue(top),
            Flags = flags,
        };

    [Fact]
    public void BareTile_OffersItsFloor()
    {
        RoomTileSection? section = RoomTileSectionFinder.Find(Floor, [], Floor, AnyStep);

        section.Should().NotBeNull();
        section!.Value.Height.Should().Be(Floor);
        section.Value.IsBareFloor.Should().BeTrue();
    }

    /// <summary>
    /// The whole point of the exercise. A platform resting two units up leaves exactly the clearance
    /// an avatar needs, so the floor beneath it stays walkable — and the platform's own top is
    /// offered as well, because both are real surfaces of the same tile.
    /// </summary>
    [Fact]
    public void PlatformWithClearance_LeavesTheFloorUnderneathStandable()
    {
        RoomTileOccupant platform = Item(1, bottom: 2, top: 3, RoomTileFlags.Walkable);

        RoomTileSection? underneath = RoomTileSectionFinder.Find(
            Floor,
            [platform],
            Floor,
            Altitude.FromValue(0)
        );

        underneath.Should().NotBeNull();
        underneath!.Value.Height.Should().Be(Floor);
        underneath.Value.IsBareFloor.Should().BeTrue();

        RoomTileSection? onTop = RoomTileSectionFinder.Find(Floor, [platform], Floor, AnyStep);

        onTop.Should().NotBeNull();
        onTop!.Value.Height.Should().Be(Altitude.FromValue(3));
        onTop.Value.ItemId.Should().Be(1);
        onTop.Value.IsWalkable.Should().BeTrue();
    }

    /// <summary>
    /// One unit of headroom is not enough to stand in, so the floor stops being a surface at all —
    /// which is what keeps the pathfinder from routing a walk into a crawlspace.
    /// </summary>
    [Fact]
    public void PlatformTooLow_ClosesTheFloorUnderneath()
    {
        RoomTileOccupant platform = Item(1, bottom: 1, top: 2, RoomTileFlags.Walkable);

        RoomTileSection? underneath = RoomTileSectionFinder.Find(
            Floor,
            [platform],
            Floor,
            Altitude.FromValue(0)
        );

        underneath.Should().BeNull();
    }

    /// <summary>
    /// A chair rests *on* the floor, so it takes the floor away rather than leaving a gap. You do
    /// not stand under a chair; you stand on it, and that is the surface one candidate up.
    /// </summary>
    [Fact]
    public void ItemRestingOnTheFloor_ReplacesItRatherThanCoveringIt()
    {
        RoomTileOccupant chair = Item(7, bottom: 0, top: 1, RoomTileFlags.Sittable);

        RoomTileSectionFinder
            .Find(Floor, [chair], Floor, Altitude.FromValue(0))
            .Should()
            .BeNull("the floor has a chair on it, not two units of air");

        RoomTileSection? seat = RoomTileSectionFinder.Find(Floor, [chair], Floor, AnyStep);

        seat.Should().NotBeNull();
        seat!.Value.Height.Should().Be(Altitude.FromValue(1));
        seat.Value.IsSittable.Should().BeTrue();
    }

    /// <summary>
    /// A rug has no thickness: its top and its bottom are both the floor it lies on. It must not
    /// block the surface it is itself forming — the bug that the "only what is overhead counts"
    /// test in the finder exists to prevent.
    /// </summary>
    [Fact]
    public void FlatItem_FormsTheSurfaceInsteadOfBlockingIt()
    {
        RoomTileOccupant rug = Item(3, bottom: 0, top: 0, RoomTileFlags.Walkable);

        RoomTileSection? section = RoomTileSectionFinder.Find(Floor, [rug], Floor, AnyStep);

        section.Should().NotBeNull();
        section!.Value.Height.Should().Be(Floor);
        section.Value.ItemId.Should().Be(3);
        section.Value.IsWalkable.Should().BeTrue();
    }

    /// <summary>
    /// A surface another item passes through is not a surface. The strict comparison is the whole
    /// distinction: the item forming a top is not straddling it.
    /// </summary>
    [Fact]
    public void SurfaceInsideAnotherItem_IsNotOffered()
    {
        RoomTileOccupant block = Item(1, bottom: 0, top: 4);
        RoomTileOccupant inside = Item(2, bottom: 0, top: 2, RoomTileFlags.Walkable);

        RoomTileSectionFinder
            .Find(Floor, [block, inside], Floor, Altitude.FromValue(2))
            .Should()
            .BeNull("2 is inside the block, and 0 has the block resting on it");
    }

    /// <summary>
    /// Reach is what makes this useful to a pathfinder: a surface further than one step is not an
    /// answer, however walkable it is.
    /// </summary>
    [Fact]
    public void SurfaceBeyondTheStep_IsOutOfReach()
    {
        RoomTileOccupant platform = Item(1, bottom: 5, top: 6, RoomTileFlags.Walkable);

        RoomTileSection? section = RoomTileSectionFinder.Find(
            Floor,
            [platform],
            Altitude.Zero,
            Altitude.FromValue(2)
        );

        section.Should().NotBeNull("the floor is still under the foot");
        section!.Value.Height.Should().Be(Floor);
    }

    /// <summary>
    /// Climbing and dropping have their own limits: a two-unit block cannot be climbed one step at
    /// a time, but you can jump off it.
    /// </summary>
    [Fact]
    public void ClimbIsLimitedSeparatelyFromTheDrop()
    {
        RoomTileOccupant block = Item(1, bottom: 0, top: 2, RoomTileFlags.Walkable);

        RoomTileSectionFinder
            .Find(Floor, [block], Floor, Altitude.FromValue(1), Altitude.FromValue(2))
            .Should()
            .BeNull("the block is two up and the climb is one");

        RoomTileSection? landing = RoomTileSectionFinder.Find(
            Floor,
            [],
            Altitude.FromValue(2),
            Altitude.FromValue(1),
            Altitude.FromValue(2)
        );

        landing.Should().NotBeNull("a two-unit drop is within the drop limit");
        landing!.Value.Height.Should().Be(Floor);
    }

    /// <summary>
    /// Two things at the same height: you stand on the one resting highest, and its flags are the
    /// ones that apply.
    /// </summary>
    [Fact]
    public void SharedTop_TakesTheFlagsOfTheItemRestingHighest()
    {
        RoomTileOccupant low = Item(1, bottom: 0, top: 2, RoomTileFlags.Walkable);
        RoomTileOccupant high = Item(2, bottom: 1, top: 2, RoomTileFlags.Sittable);

        RoomTileSection? section = RoomTileSectionFinder.Find(Floor, [low, high], Floor, AnyStep);

        section.Should().NotBeNull();
        section!.Value.ItemId.Should().Be(2);
        section.Value.IsSittable.Should().BeTrue();
    }

    /// <summary>
    /// <see cref="RoomTileSectionFinder.FindTop" /> must keep answering what the tile has always
    /// answered — the highest surface — because that is the one the client is told about in its
    /// height map, and it must not move for a change the client knows nothing about.
    /// </summary>
    [Fact]
    public void FindTop_StillAnswersTheHighestSurface()
    {
        RoomTileOccupant rug = Item(1, bottom: 0, top: 0, RoomTileFlags.Walkable);
        RoomTileOccupant platform = Item(2, bottom: 4, top: 5, RoomTileFlags.Walkable);

        RoomTileSection? top = RoomTileSectionFinder.FindTop(Floor, [rug, platform]);

        top.Should().NotBeNull();
        top!.Value.Height.Should().Be(Altitude.FromValue(5));
        top.Value.ItemId.Should().Be(2);
    }
}
