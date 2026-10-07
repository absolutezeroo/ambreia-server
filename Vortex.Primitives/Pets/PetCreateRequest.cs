using Orleans;
using Vortex.Primitives.Rooms.Enums;

namespace Vortex.Primitives.Pets;

[GenerateSerializer, Immutable]
public sealed record PetCreateRequest
{
    [Id(0)]
    public required string Name { get; init; }

    [Id(1)]
    public required int Type { get; init; }

    [Id(2)]
    public required int Skin { get; init; }

    [Id(3)]
    public required string Color { get; init; }

    [Id(4)]
    public required AvatarGenderType Gender { get; init; }

    // 5 and 6 were the starting energy and nutrition. A pet has no needs since M0, so it starts
    // at the entity's own maximum.
}
