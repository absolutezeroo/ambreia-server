using Orleans;

namespace Vortex.Primitives.Rpg.Characters;

/// <summary>Who a character is: its content ids and its progress.</summary>
[GenerateSerializer, Immutable]
public sealed record CharacterSummary
{
    [Id(0)]
    public required string ClassId { get; init; }

    [Id(1)]
    public required string SpecializationId { get; init; }

    [Id(2)]
    public required int Level { get; init; }

    /// <summary>XP toward the next level; 0 at the max level.</summary>
    [Id(3)]
    public required int Xp { get; init; }
}
