using Orleans;

namespace Vortex.Primitives.Rpg.Characters;

/// <summary>
/// What a zone reads of a character when it enters (spec M1a §4.4, §7): its stats, current health and
/// mana (regenerated out of any zone, bounded to the maximum), level, attack range and threat
/// multiplier from its class.
/// </summary>
[GenerateSerializer, Immutable]
public sealed record CharacterCombatProfile
{
    [Id(0)]
    public required CharacterStatsSnapshot Stats { get; init; }

    [Id(1)]
    public required int Health { get; init; }

    [Id(2)]
    public required int Mana { get; init; }

    [Id(3)]
    public required int Level { get; init; }

    /// <summary>Attack range in tiles, from the class.</summary>
    [Id(4)]
    public required int Range { get; init; }

    [Id(5)]
    public required int ThreatMultiplier { get; init; }
}
