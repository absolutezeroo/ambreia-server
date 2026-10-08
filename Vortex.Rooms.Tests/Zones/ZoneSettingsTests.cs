using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Layers;
using Vortex.Rooms.Grains.Zones.Compilation;
using Vortex.Rooms.Tests.Support;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Zones;

/// <summary>
/// A zone's settings are the room's (plan D1, D2): its name, its capacity — the entry check reads
/// PlayersMax — and its walls. In memory: the rooms row belongs to the grain and stays as it is.
/// </summary>
public sealed class ZoneSettingsTests
{
    [Fact]
    public async Task TheSettings_BecomeTheRoomsNameCapacityAndWalls()
    {
        ZoneDocument doc = ZoneSamples.Minimal() with
        {
            Settings = new ZoneSettings
            {
                Id = "village",
                Name = "Village d'Ambre",
                Region = "ambre",
                Capacity = 12,
            },
            Walls = new ZoneWalls
            {
                Visible = false,
                Height = 3,
                Thickness = 1,
            },
        };

        RoomHarness harness = await RoomHarness.CreateAsync();

        RoomSnapshot room = ZoneSettingsApplier.Apply(harness.Grain._state.RoomSnapshot, doc);

        room.Name.Should().Be("Village d'Ambre");
        room.Description.Should().Be("ambre");
        room.PlayersMax.Should().Be(12);
        room.HideWalls.Should().BeTrue();
        room.WallHeight.Should().Be(3);
        room.WallThickness.Should().Be(RoomThicknessType.Thick);
    }

    [Fact]
    public async Task AZoneRoom_CarriesItsDocumentsCapacity()
    {
        RoomHarness harness = await RoomHarness.CreateZoneAsync(
            ZoneSamples.Minimal() with
            {
                Settings = new ZoneSettings
                {
                    Id = "village",
                    Name = "Village",
                    Capacity = 1,
                },
            }
        );

        harness.Grain.ApplyZoneSettings();

        harness.Grain._state.RoomSnapshot.PlayersMax.Should().Be(1);
        harness.Grain._state.RoomSnapshot.Name.Should().Be("Village");
    }
}
