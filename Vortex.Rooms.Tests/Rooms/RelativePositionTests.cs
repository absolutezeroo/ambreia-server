using System;
using System.Linq;
using FluentAssertions;
using Vortex.Primitives.Rooms.Enums;
using Xunit;

namespace Vortex.Rooms.Tests.Rooms;

/// <summary>
/// Where somebody stands in relation to a piece of furniture.
/// </summary>
/// <remarks>
/// The whole point is that this is measured from the furniture rather than from the room: content
/// says "in front of the television" and must keep meaning it after a builder turns the television
/// round. A version that answered compass directions would pass a careless reading of every one of
/// these cases and be wrong in every room but the one it was written in.
/// </remarks>
public sealed class RelativePositionTests
{
    /// <summary>The furniture sits at 5,5 throughout; only the standing tile and the facing move.</summary>
    private static RelativePosition? At(int x, int y, Rotation facing) =>
        RelativePositions.Of(x, y, 5, 5, facing);

    [Fact]
    public void The_tile_a_piece_of_furniture_faces_is_its_front()
    {
        // North is -Y, so the tile in front of a north-facing chair is the one above it.
        At(5, 4, Rotation.North).Should().Be(RelativePosition.Front);
        At(5, 6, Rotation.North).Should().Be(RelativePosition.Back);
    }

    [Fact]
    public void Turning_the_furniture_takes_its_front_with_it()
    {
        // The same tile, four different chairs. This is the test the whole design exists for: a
        // compass answer would have said "north" to all four.
        //
        // Read them from the chair's seat rather than from the map. Facing east, the tile to the
        // north is off your left shoulder; facing west, the same tile is off your right.
        At(5, 4, Rotation.North).Should().Be(RelativePosition.Front);
        At(5, 4, Rotation.East).Should().Be(RelativePosition.Left);
        At(5, 4, Rotation.South).Should().Be(RelativePosition.Back);
        At(5, 4, Rotation.West).Should().Be(RelativePosition.Right);
    }

    [Theory]
    // Around a south-facing chair, one tile at a time, clockwise from its own front.
    [InlineData(5, 6, RelativePosition.Front)]
    [InlineData(4, 6, RelativePosition.FrontRight)]
    [InlineData(4, 5, RelativePosition.Right)]
    [InlineData(4, 4, RelativePosition.BackRight)]
    [InlineData(5, 4, RelativePosition.Back)]
    [InlineData(6, 4, RelativePosition.BackLeft)]
    [InlineData(6, 5, RelativePosition.Left)]
    [InlineData(6, 6, RelativePosition.FrontLeft)]
    public void Every_tile_around_it_has_its_own_side(int x, int y, RelativePosition expected) =>
        At(x, y, Rotation.South).Should().Be(expected);

    [Fact]
    public void Standing_on_it_is_its_own_answer()
    {
        // Not "front at distance zero": sitting on the chair and standing before it are two
        // different things to write content about, and one filter has to tell them apart.
        At(5, 5, Rotation.South).Should().Be(RelativePosition.On);
        At(5, 5, Rotation.None).Should().Be(RelativePosition.On);
    }

    [Theory]
    [InlineData(5, 3)]
    [InlineData(7, 5)]
    [InlineData(3, 3)]
    public void Beyond_one_tile_there_is_no_relation_at_all(int x, int y) =>
        At(x, y, Rotation.South).Should().BeNull();

    [Fact]
    public void A_piece_with_no_rotation_is_measured_from_north()
    {
        // A rug has no front. Answering from north keeps the filter honest rather than refusing to
        // answer at all -- and it is the same reading the room gives a rotation of None elsewhere.
        At(5, 4, Rotation.None).Should().Be(RelativePosition.Front);
        At(6, 5, Rotation.None).Should().Be(RelativePosition.Right);
    }

    [Fact]
    public void Every_position_names_itself_exactly_once()
    {
        // The strings are what content stores, so a duplicate would silently merge two sides.
        RelativePosition[] all = Enum.GetValues<RelativePosition>();

        all.Select(p => p.ToValue()).Should().OnlyHaveUniqueItems().And.HaveCount(all.Length);
    }
}
