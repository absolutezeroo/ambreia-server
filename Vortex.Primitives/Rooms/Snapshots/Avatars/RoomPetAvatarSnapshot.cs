using Orleans;

namespace Vortex.Primitives.Rooms.Snapshots.Avatars;

[GenerateSerializer, Immutable]
public sealed record RoomPetAvatarSnapshot : RoomAvatarSnapshot
{
    [Id(12)]
    public required int SubType { get; init; }

    [Id(13)]
    public required int OwnerId { get; init; }

    [Id(14)]
    public required string OwnerName { get; init; }

    // 16 and 19 to 22 were the monsterplant rarity and the breed, harvest and revive flags, removed
    // in M0. The serializer still writes them, as constants.

    [Id(17)]
    public required bool HasSaddle { get; init; }

    [Id(18)]
    public required bool IsRiding { get; init; }

    [Id(23)]
    public required int PetLevel { get; init; }

    [Id(24)]
    public required string PetPosture { get; init; }
}
