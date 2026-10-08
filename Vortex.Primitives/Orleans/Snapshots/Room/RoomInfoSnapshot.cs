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

    // [Id(2)] retired: TradeType, a Habbo room setting; trade rules come from the zone document.
    // [Id(3)] retired: Score, last read by the room rating packet the RPG no longer sends.
    // [Id(4)] retired: Ranking, navigator ordering (the navigator is gone, spec §3.2 ter).
    // [Id(5)] retired: CategoryId, navigator category.
    // [Id(6)] retired: Tags, navigator search.

    [Id(7)]
    public required bool AllowBlocking { get; init; } = false;

    // [Id(8)] retired: AllowPets, a Habbo room setting nothing read.
    // [Id(9)] retired: AllowPetsEat, a Habbo room setting nothing read.

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
