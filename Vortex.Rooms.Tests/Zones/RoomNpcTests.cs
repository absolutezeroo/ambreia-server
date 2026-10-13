using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object.Avatars;
using Vortex.Primitives.Rooms.Snapshots.Avatars;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Layers;
using Vortex.Rooms.Tests.Support;
using Vortex.Tests.Support.World;
using Xunit;

namespace Vortex.Rooms.Tests.Zones;

/// <summary>Spec M2a §3.3: a zone's NPCs stand in its room, as Habbo bots, and never walk.</summary>
public sealed class RoomNpcTests
{
    private static ZoneDocument WithNpcs(params ZoneNpc[] npcs) =>
        ZoneSamples.Minimal() with
        {
            Npcs = [.. npcs],
        };

    private static ZoneNpc Elder(int x = 2, int y = 2) =>
        new()
        {
            Id = "elder",
            Template = "npc.elder",
            Name = "Ancien du camp",
            Look = "hd-180-1.ch-210-66",
            Cell = new ZoneCell(x, y),
            Rotation = 4,
            Dialogue = "dialogue.camp.elder",
        };

    [Fact]
    public async Task AZonesNpcs_AreSpawned_Once_AsBots()
    {
        RoomHarness harness = await RoomHarness.CreateZoneAsync(WithNpcs(Elder()));

        harness.Grain.NpcModule.EnsureSpawned();
        harness.Grain.NpcModule.EnsureSpawned();

        IRoomNpc npc = harness.Grain.NpcModule.Npcs.Should().ContainSingle().Subject;
        (npc.NpcId, npc.X, npc.Y, npc.Dialogue).Should().Be(("elder", 2, 2, "dialogue.camp.elder"));
        RoomBotAvatarSnapshot snapshot = npc.GetSnapshot()
            .Should()
            .BeOfType<RoomBotAvatarSnapshot>()
            .Subject;
        (snapshot.AvatarType, snapshot.Name, snapshot.Figure, snapshot.WebId)
            .Should()
            .Be((RoomObjectType.Bot, "Ancien du camp", "hd-180-1.ch-210-66", -npc.ObjectId.Value));
        harness.Grain.NpcModule.NpcAt(npc.ObjectId.Value).Should().BeSameAs(npc);
    }

    [Fact]
    public async Task AnNpcOutsideTheMap_IsSkipped()
    {
        RoomHarness harness = await RoomHarness.CreateZoneAsync(
            WithNpcs(Elder(), Elder(99, 99) with { Id = "lost" })
        );

        harness.Grain.NpcModule.EnsureSpawned();

        harness.Grain.NpcModule.Npcs.Select(n => n.NpcId).Should().Equal("elder");
    }

    [Fact]
    public async Task AnOrdinaryRoom_HasNoNpc()
    {
        RoomHarness harness = await RoomHarness.CreateAsync();

        harness.Grain.NpcModule.EnsureSpawned();

        harness.Grain.NpcModule.Npcs.Should().BeEmpty();
    }
}
