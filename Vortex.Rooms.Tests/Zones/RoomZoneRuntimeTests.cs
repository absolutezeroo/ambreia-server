using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Avatars;
using Vortex.Primitives.Rooms.Snapshots.Avatars;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Layers;
using Vortex.Protocol.Messages.Outgoing.Room.Engine;
using Vortex.Rooms.Grains.Zones.Runtime;
using Vortex.Rooms.Tests.Support;
using Vortex.Rpg.Zones;
using Vortex.Tests.Support.World;
using Xunit;

namespace Vortex.Rooms.Tests.Zones;

/// <summary>Spec M1a §4.2: the room as a zone for combat — monsters as pets, sight, walking within
/// each avatar's rules and the path budget, where a dead player stands up again.</summary>
public sealed class RoomZoneRuntimeTests
{
    private static readonly MonsterAppearance Wolf = new("monster.forest.wolf", "0 3 C0A060", 2);

    private RoomHarness _harness = null!;

    private RoomZoneRuntime Runtime => _harness.Grain.CombatRuntime;

    private async Task ZoneAsync(ZoneDocument? zone = null)
    {
        _harness = await RoomHarness.CreateZoneAsync(zone ?? ZoneSamples.Minimal());
        Runtime.BeginTick(1000);
    }

    [Fact]
    public async Task ASpawnedMonster_IsAPetInUsers()
    {
        await ZoneAsync();

        int id = Runtime.SpawnMonster(Wolf, 3, 3);
        await Runtime.FlushAsync();

        RoomPetAvatarSnapshot pet = _harness
            .BroadcastToRoom.OfType<UsersMessageComposer>()
            .SelectMany(u => u.Avatars)
            .OfType<RoomPetAvatarSnapshot>()
            .Single();
        (pet.AvatarType, pet.WebId, pet.Name, pet.Figure, pet.PetLevel, pet.OwnerId)
            .Should()
            .Be((RoomObjectType.Pet, -id, Wolf.DefinitionId, Wolf.Figure, 2, 0));
        Runtime.PositionOf(id).Should().Be((3, 3));
        Runtime.IsFree(3, 3).Should().BeFalse("the wolf stands there");
    }

    [Fact]
    public async Task ARemovedMonster_LeavesTheRoom()
    {
        await ZoneAsync();
        int id = Runtime.SpawnMonster(Wolf, 3, 3);

        Runtime.RemoveMonster(id);
        await Runtime.FlushAsync();

        _harness
            .BroadcastToRoom.OfType<UserRemoveMessageComposer>()
            .Single()
            .ObjectId.Should()
            .Be(new RoomObjectId(id));
        Runtime.PositionOf(id).Should().BeNull();
        Runtime.IsFree(3, 3).Should().BeTrue();
    }

    [Fact]
    public async Task LineOfSight_IsCutByAHole()
    {
        ZoneDocument zone = ZoneSamples.Minimal();
        ZoneTile[] tiles = [.. zone.Floor.Tiles];
        tiles[2 * 6 + 2] = new ZoneTile { Hole = true };
        await ZoneAsync(zone with { Floor = zone.Floor with { Tiles = [.. tiles] } });

        Runtime.HasLineOfSight(0, 2, 4, 2).Should().BeFalse("(2, 2) is a hole");
        Runtime.HasLineOfSight(0, 4, 4, 4).Should().BeTrue();
    }

    [Fact]
    public async Task AMonsterPath_AvoidsSafeCells()
    {
        // A safe wall at x = 2 from y = 0 to 4: the only way round is through y = 5.
        await ZoneAsync(
            ZoneSamples.Minimal() with
            {
                Zones =
                [
                    new ZoneArea
                    {
                        Id = "town",
                        Kind = ZoneAreaKinds.Safe,
                        Polygon = [new(2, 0), new(2, 0), new(2, 4), new(2, 4)],
                    },
                ],
            }
        );
        Runtime.RulesAt(2, 2).Safe.Should().BeTrue("the test's safe wall");
        Runtime.RulesAt(2, 5).Safe.Should().BeFalse("the way round");
        int id = Runtime.SpawnMonster(Wolf, 0, 0);

        Runtime.WalkNear(id, 4, 0, 0).Should().Be(WalkResult.Started);

        IRoomAvatar wolf = _harness.Grain._state.AvatarsByObjectId[new RoomObjectId(id)];
        wolf.TilePath.Select(_harness.Grain.MapModule.GetTileXY)
            .Should()
            .NotContain(c => c.Item1 == 2 && c.Item2 < 5);
    }

    [Fact]
    public async Task ThePathBudget_IsPerTick()
    {
        await ZoneAsync();
        int id = Runtime.SpawnMonster(Wolf, 0, 0);

        for (int i = 0; i < 8; i++)
        {
            Runtime.WalkNear(id, 5, i % 6, 0).Should().Be(WalkResult.Started);
        }

        Runtime.WalkNear(id, 5, 5, 0).Should().Be(WalkResult.BudgetSpent);

        Runtime.BeginTick(1250);
        Runtime.WalkNear(id, 5, 5, 0).Should().Be(WalkResult.Started);
    }

    [Fact]
    public async Task RespawnCell_IsTheRespawnEntry_ElseTheArrival()
    {
        ZoneEntry arrival = new()
        {
            Id = "door",
            Kind = ZoneEntryKinds.Arrival,
            Cell = new ZoneCell(0, 2),
        };
        await ZoneAsync(ZoneSamples.Minimal() with { Entries = [arrival] });
        Runtime.RespawnCell().Should().Be((0, 2));

        await ZoneAsync(
            ZoneSamples.Minimal() with
            {
                Entries =
                [
                    arrival,
                    new ZoneEntry
                    {
                        Id = "grave",
                        Kind = ZoneEntryKinds.Respawn,
                        Cell = new ZoneCell(4, 4),
                    },
                ],
            }
        );
        Runtime.RespawnCell().Should().Be((4, 4));
    }

    [Fact]
    public async Task AGhost_LiesAndCannotWalk()
    {
        await ZoneAsync();
        IRoomAvatar player = _harness.PutRealPlayerInRoom(RoomHarness.Owner, 1, 1);

        Runtime.SetPosture(player.ObjectId.Value, "lay");

        (player.HasStatus(AvatarStatusType.Lay), player.IsMovementLocked).Should().Be((true, true));

        Runtime.SetPosture(player.ObjectId.Value, null);

        (player.HasStatus(AvatarStatusType.Lay), player.IsMovementLocked)
            .Should()
            .Be((false, false));
    }
}
