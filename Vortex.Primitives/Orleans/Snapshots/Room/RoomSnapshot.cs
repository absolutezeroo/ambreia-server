using Orleans;
using Vortex.Primitives.Orleans.Snapshots.Room.Settings;
using Vortex.Primitives.Rooms.Enums;

namespace Vortex.Primitives.Orleans.Snapshots.Room;

[GenerateSerializer, Immutable]
public sealed record RoomSnapshot : RoomInfoSnapshot
{
    [Id(0)]
    public required string Password { get; init; } = string.Empty;

    [Id(1)]
    public required ModSettingsSnapshot ModSettings { get; init; }

    [Id(2)]
    public required ChatSettingsSnapshot ChatSettings { get; init; }

    [Id(3)]
    public required string WorldType { get; init; } = string.Empty;

    [Id(4)]
    public required bool HideWalls { get; init; }

    [Id(5)]
    public required RoomThicknessType WallThickness { get; init; }

    [Id(6)]
    public required RoomThicknessType FloorThickness { get; init; }

    [Id(7)]
    public required int MaxVisitorsLimit { get; init; }

    // The four 701 toggles (door-tile exit, idle sleep, idle autokick, pet mute) and the two
    // timeouts. Not `required`: every existing construction site predates them, and the defaults here
    // are the ones the serializer used to write as constants. Copied from the row; nothing reads
    // them today.
    [Id(8)]
    public bool LeaveOnDoorTile { get; init; }

    [Id(9)]
    public bool IdleSleepEnabled { get; init; } = true;

    [Id(10)]
    public int IdleSleepTimeoutSeconds { get; init; } = 300;

    [Id(11)]
    public bool IdleAutokickEnabled { get; init; }

    [Id(12)]
    public int IdleAutokickTimeoutSeconds { get; init; } = 1800;

    [Id(13)]
    public bool MuteAllPets { get; init; }

    /// <summary>
    /// The floor plan editor's wall height: -1 lets the client raise the walls above the highest
    /// floor tile, anything else fixes them (<c>BCFloorPlanEditor</c> sends -1 when the option is
    /// off, and <c>FloorHeightMap</c> carries it back on entry).
    /// </summary>
    [Id(14)]
    public int WallHeight { get; init; } = -1;
}
