using System.Linq;
using FluentAssertions;
using Vortex.Rooms.Grains.Systems;
using Xunit;

namespace Vortex.Rooms.Tests.Pets;

/// <summary>
/// The two pieces of arithmetic a companion follows its owner by: how far is too far, and which
/// tiles count as beside them.
/// </summary>
public sealed class PetFollowTests
{
    [Theory]
    [InlineData(3, 3, true)]
    [InlineData(3, 0, true)]
    [InlineData(4, 0, false)]
    [InlineData(0, 4, false)]
    public void IsWithin_CountsADiagonalAsOneStep(int dx, int dy, bool expected) =>
        RoomPetRuntime.IsWithin(5, 5, 5 + dx, 5 + dy, 3).Should().Be(expected);

    [Fact]
    public void Neighbours_AreTheEightTilesAroundButNotTheTileItself()
    {
        (int X, int Y)[] tiles = [.. RoomPetRuntime.Neighbours(2, 2)];

        tiles.Should().HaveCount(8).And.OnlyHaveUniqueItems().And.NotContain((2, 2));
        tiles.Should().OnlyContain(t => RoomPetRuntime.IsWithin(2, 2, t.X, t.Y, 1));
    }
}
