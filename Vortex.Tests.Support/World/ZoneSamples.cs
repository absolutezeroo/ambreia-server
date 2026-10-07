using System.Linq;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Layers;
using Vortex.Primitives.World.Rules;

namespace Vortex.Tests.Support;

/// <summary>Small valid zone documents for tests: a 6 × 6 open floor, door at (0, 2).</summary>
public static class ZoneSamples
{
    public const int Throne = 230;
    public const int Poster = 4001;

    public static ZoneDocument Minimal(string id = "village") =>
        new()
        {
            Settings = new ZoneSettings { Id = id, Name = "Village" },
            Floor = new ZoneFloor
            {
                Width = 6,
                Length = 6,
                Tiles = [.. Enumerable.Range(0, 36).Select(_ => new ZoneTile())],
                Door = new ZoneCell(0, 2),
            },
        };

    public static ZoneDocument EveryLayer() =>
        Minimal() with
        {
            Decor =
            [
                new ZoneDecorObject
                {
                    Id = "throne_1",
                    Definition = Throne,
                    Class = "throne",
                    X = 3,
                    Y = 3,
                },
            ],
            Entries =
            [
                new ZoneEntry
                {
                    Id = "arrival",
                    Kind = "arrival",
                    Cell = new ZoneCell(0, 2),
                },
            ],
            Zones =
            [
                new ZoneArea
                {
                    Id = "z_safe",
                    Kind = "Safe",
                    Polygon = [new(0, 0), new(5, 0), new(5, 5), new(0, 5)],
                },
            ],
            Spawns =
            [
                new ZoneSpawn
                {
                    Id = "spawn_1",
                    Area = [new(4, 4), new(5, 4), new(5, 5), new(4, 5)],
                    Monster = "wolf",
                },
            ],
            Npcs =
            [
                new ZoneNpc
                {
                    Id = "npc_1",
                    Template = "blacksmith",
                    Cell = new ZoneCell(1, 1),
                },
            ],
            Triggers =
            [
                new ZoneTrigger
                {
                    Id = "t_enter",
                    Type = ZoneTriggerType.EnterZone,
                    Target = "z_safe",
                    Conditions = [new PlayerLevelCondition(1, 99)],
                    Actions = [new ActivateZoneAction("z_safe")],
                },
            ],
        };
}
