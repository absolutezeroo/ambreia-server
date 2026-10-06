using FluentAssertions;
using Vortex.Rooms.Grains.Systems;
using Xunit;

namespace Vortex.Rooms.Tests.Pets;

/// <summary>
/// A pet sent to its nest (command 13) walks to the nearest one. Covers the choice the walk is built
/// on; the pathing itself belongs to the room tick and is checked in game. Pets no longer tire on
/// their own since M0, so the nest is only ever an order.
/// </summary>
public sealed class PetNestSeekingTests
{
    [Fact]
    public void PickNearestTile_SeveralNests_TakesTheClosest()
    {
        (int X, int Y)? nest = RoomPetRuntime.PickNearestTile(
            fromX: 1,
            fromY: 1,
            [(9, 9), (3, 2), (0, 8)]
        );

        nest.Should().Be((3, 2));
    }

    [Fact]
    public void PickNearestTile_EquidistantNests_KeepsTheFirst()
    {
        (int X, int Y)? nest = RoomPetRuntime.PickNearestTile(fromX: 0, fromY: 0, [(2, 0), (0, 2)]);

        nest.Should().Be((2, 0), "a stable pick stops the pet dithering between two nests");
    }

    [Fact]
    public void PickNearestTile_NoNestInTheRoom_ReturnsNull()
    {
        RoomPetRuntime.PickNearestTile(fromX: 4, fromY: 4, []).Should().BeNull();
    }
}
