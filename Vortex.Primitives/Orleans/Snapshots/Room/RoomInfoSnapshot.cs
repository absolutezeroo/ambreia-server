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

    [Id(3)]
    public required int Score { get; init; } = 0;

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

    [Id(16)]
    public required bool StaffPick { get; init; } = false;

    /// <summary>Name of the room's currently running advertisement, empty when it has none. The
    /// client reads this trio behind bit 4 of the room bitmask and renders it as the room-ad panel;
    /// leaving it empty is what made a purchased ad invisible everywhere it was sold for.</summary>
    [Id(17)]
    public string AdName { get; init; } = string.Empty;

    [Id(18)]
    public string AdDescription { get; init; } = string.Empty;

    /// <summary>When the advertisement runs out. Kept as the instant rather than as the minutes the
    /// client wants, because a snapshot outlives the moment it was built and a stored countdown
    /// would be wrong by however long it sat in a room grain.</summary>
    [Id(19)]
    public DateTime? AdExpiresAt { get; init; }
}
