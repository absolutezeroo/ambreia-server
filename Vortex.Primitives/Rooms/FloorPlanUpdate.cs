using Orleans;

namespace Vortex.Primitives.Rooms;

/// <summary>
/// A save from the Builders Club floor-plan editor.
///
/// Every field but <see cref="Model" /> is optional on the wire — the client's composer sends the
/// plan alone, six fields, or seven, depending on which of its own arguments were left at -1 — so
/// -1 here means "leave whatever the room already has" rather than a value.
/// </summary>
[GenerateSerializer, Immutable]
public sealed record FloorPlanUpdate
{
    /// <summary>The plan itself: one row per line, one character per tile, base 33 for the height
    /// and <c>x</c> for a hole.</summary>
    [Id(0)]
    public required string Model { get; init; }

    [Id(1)]
    public required int DoorX { get; init; }

    [Id(2)]
    public required int DoorY { get; init; }

    [Id(3)]
    public required int DoorRotation { get; init; }

    /// <summary>-2, -1, 0 or 1 — the wire's own multipliers, not the editor's dropdown index.
    /// See <c>RoomThicknessType</c>.</summary>
    [Id(4)]
    public required int WallThickness { get; init; }

    [Id(5)]
    public required int FloorThickness { get; init; }

    /// <summary>-1 unless the editor's "wall height" box is ticked, in which case the walls stop
    /// being derived from the plan and take this instead.</summary>
    [Id(6)]
    public required int WallHeight { get; init; }
}
