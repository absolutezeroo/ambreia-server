using System.Collections.Generic;
using Vortex.Primitives.World.Layers;

namespace Vortex.Primitives.World;

/// <summary>
/// A zone as the map editor builds it and the server runs it: every layer of the editor, the RPG
/// ones included, so a zone built before a system exists stays valid once it does.
/// </summary>
/// <remarks>Spec: docs/superpowers/specs/2026-10-07-rpg-world-map-editor-design.md §4.</remarks>
public sealed record ZoneDocument
{
    public const int CurrentSchemaVersion = 1;
    public const int MaxSize = 64;
    public const int MaxTileHeight = 32;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public required ZoneSettings Settings { get; init; }
    public required ZoneFloor Floor { get; init; }
    public ZoneWalls Walls { get; init; } = new();
    public List<ZoneDecorObject> Decor { get; init; } = [];
    public List<ZoneWallDecorObject> WallDecor { get; init; } = [];
    public List<ZoneEntry> Entries { get; init; } = [];
    public List<ZonePassage> Passages { get; init; } = [];
    public List<ZoneArea> Zones { get; init; } = [];
    public List<ZoneNpc> Npcs { get; init; } = [];
    public List<ZoneSpawn> Spawns { get; init; } = [];
    public List<ZoneResource> Resources { get; init; } = [];
    public List<ZoneChest> Chests { get; init; } = [];
    public List<ZoneInteractive> Interactives { get; init; } = [];
    public List<ZoneBoss> Bosses { get; init; } = [];
    public List<ZoneEvent> Events { get; init; } = [];
    public List<ZoneTrigger> Triggers { get; init; } = [];
    public ZoneAmbience Ambience { get; init; } = new();
    public List<ZonePrefabUse> Prefabs { get; init; } = [];
}
