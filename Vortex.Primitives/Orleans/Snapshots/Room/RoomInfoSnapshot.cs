using System;
using System.Collections.Immutable;
using Orleans;
using Vortex.Primitives.Rooms.Enums;

namespace Vortex.Primitives.Orleans.Snapshots.Room;

[GenerateSerializer, Immutable]
public record RoomInfoSnapshot : RoomSummarySnapshot
{
    [Id(0)]
    public required RoomDoorModeType DoorMode { get; init; } = RoomDoorModeType.Invisible;

    [Id(1)]
    public required int PlayersMax { get; init; } = 0;

    [Id(2)]
    public required RoomTradeModeType TradeType { get; init; } = RoomTradeModeType.Disabled;

    // [Id(3)] retired: Score, last read by the room rating packet the RPG no longer sends.

    [Id(4)]
    public required int Ranking { get; init; } = 0;

    [Id(5)]
    public required int CategoryId { get; init; } = -1;

    [Id(6)]
    public required ImmutableArray<string> Tags { get; init; } = [];

    [Id(7)]
    public required bool AllowBlocking { get; init; } = false;

    [Id(8)]
    public required bool AllowPets { get; init; } = false;

    [Id(9)]
    public required bool AllowPetsEat { get; init; } = false;

    [Id(10)]
    public int? GroupId { get; init; }

    [Id(11)]
    public string? GroupName { get; init; }

    [Id(12)]
    public string? GroupBadge { get; init; }

    [Id(13)]
    public required string PaintWall { get; init; } = string.Empty;

    [Id(14)]
    public required string PaintFloor { get; init; } = string.Empty;

    [Id(15)]
    public required string PaintLandscape { get; init; } = string.Empty;
}
