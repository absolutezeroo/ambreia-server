using Orleans;
using Vortex.Primitives.Rooms.Enums;

namespace Vortex.Primitives.Rooms.Snapshots.Avatars;

[GenerateSerializer, Immutable]
public sealed record RoomPlayerAvatarSnapshot : RoomAvatarSnapshot
{
    [Id(12)]
    public required AvatarGenderType Gender { get; init; }

    // [Id(13)] retired: DanceType, a Habbo dance; nothing in the RPG dances.

    // [Id(14)]-[Id(17)] retired: favourite group id, status, name and swimsuit figure, constants
    // since the cut of 2026-10-07 and no longer on the wire. Never reuse them.

    [Id(18)]
    public required int ActivityPoints { get; init; }

    [Id(19)]
    public required bool IsModerator { get; init; }

    [Id(20)]
    public int CurrentEffectId { get; init; }

    // [Id(21)] retired: the badge leaderboard rank, always -1 and no longer on the wire.

    /// <summary>What the avatar is holding; zero for empty-handed. Rides its own composer rather
    /// than the avatar block, so a room replays it for whoever has just walked in.</summary>
    [Id(22)]
    public int CarryItemId { get; init; }
}
