using FluentAssertions;
using Xunit;

namespace Vortex.Rooms.Tests.Furniture;

/// <summary>
/// The magic tile widget's up and down arrows (header 3315), which were unmapped and did nothing.
/// </summary>
/// <remarks>
/// The slider and the text box next to them already send an absolute height, so these two only need
/// a server because the step is "the next surface on this tile" — something the client cannot know.
/// <c>CustomStackHeightWidget.sendAdjacentHeightRequest</c> sends the direction and nothing else.
/// </remarks>
public sealed class AdjacentStackHeightTests
{
    private const double Max = 40;

    /// <summary>A crate at floor level: its underside is 0, its top 1.</summary>
    private static readonly double[] Surfaces = [0, 0, 1, 1, 2.5];

    [Fact]
    public void Up_TakesTheNearestSurfaceAbove()
    {
        AdjacentStackHeight.Pick(0, Surfaces, down: false, Max).Should().Be(1);
        AdjacentStackHeight.Pick(1, Surfaces, down: false, Max).Should().Be(2.5);
    }

    [Fact]
    public void Down_TakesTheNearestSurfaceBelow()
    {
        AdjacentStackHeight.Pick(2.5, Surfaces, down: true, Max).Should().Be(1);
        AdjacentStackHeight.Pick(1, Surfaces, down: true, Max).Should().Be(0);
    }

    [Fact]
    public void ASurfaceLevelWithTheTile_IsNotAMove()
    {
        // Strictly past, or the arrow would "succeed" while nothing changed on screen and the
        // player would keep pressing it.
        AdjacentStackHeight.Pick(2.5, Surfaces, down: false, Max).Should().BeNull();
        AdjacentStackHeight.Pick(0, Surfaces, down: true, Max).Should().BeNull();
    }

    [Fact]
    public void NothingElseOnTheTile_LeavesTheArrowsWithNowhereToGo()
    {
        AdjacentStackHeight.Pick(1, [], down: false, Max).Should().BeNull();
        AdjacentStackHeight.Pick(1, [], down: true, Max).Should().BeNull();
    }

    [Fact]
    public void TheRoomCeilingIsNotOffered()
    {
        AdjacentStackHeight.Pick(1, [Max + 1], down: false, Max).Should().BeNull();
        AdjacentStackHeight.Pick(1, [Max, Max + 1], down: false, Max).Should().Be(Max);
    }

    [Fact]
    public void ANegativeSurfaceIsNotOffered()
    {
        // The floor of a tile in a pit is a base height, and a negative one would put the item
        // under the room.
        AdjacentStackHeight.Pick(1, [-1], down: true, Max).Should().BeNull();
    }
}
