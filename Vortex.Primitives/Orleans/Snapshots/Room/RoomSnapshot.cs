using Orleans;
using Vortex.Primitives.Rooms.Enums;

namespace Vortex.Primitives.Orleans.Snapshots.Room;

[GenerateSerializer, Immutable]
public sealed record RoomSnapshot : RoomInfoSnapshot
{
    [Id(0)]
    public required string Password { get; init; } = string.Empty;

    // [Id(1)] retired: ModSettings, Habbo room moderation rights nothing read.
    // [Id(2)] retired: ChatSettings, Habbo chat bubble settings nothing read (chat comes back in F3c).

    [Id(3)]
    public required string WorldType { get; init; } = string.Empty;

    [Id(4)]
    public required bool HideWalls { get; init; }

    [Id(5)]
    public required RoomThicknessType WallThickness { get; init; }

    [Id(6)]
    public required RoomThicknessType FloorThickness { get; init; }

    // [Id(7)] retired: MaxVisitorsLimit, a configured cap nothing enforced (PlayersMax is the cap).
    // [Id(8)] retired: LeaveOnDoorTile, a Habbo room toggle nothing read.
    // [Id(9)] retired: IdleSleepEnabled.
    // [Id(10)] retired: IdleSleepTimeoutSeconds.
    // [Id(11)] retired: IdleAutokickEnabled.
    // [Id(12)] retired: IdleAutokickTimeoutSeconds.
    // [Id(13)] retired: MuteAllPets.

    /// <summary>
    /// The floor plan editor's wall height: -1 lets the client raise the walls above the highest
    /// floor tile, anything else fixes them (<c>BCFloorPlanEditor</c> sends -1 when the option is
    /// off, and <c>FloorHeightMap</c> carries it back on entry).
    /// </summary>
    [Id(14)]
    public int WallHeight { get; init; } = -1;
}
