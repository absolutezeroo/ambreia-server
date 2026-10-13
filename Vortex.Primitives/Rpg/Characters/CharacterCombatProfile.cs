using System.Collections.Immutable;
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

    /// <summary>For the skills it knows (spec M1b §9).</summary>
    [Id(6)]
    public required string ClassId { get; init; }

    [Id(7)]
    public required string SpecializationId { get; init; }

    /// <summary>Persistent statuses handed back by the last zone, with their time left; given once
    /// (spec M1b §7.4). Never read from a relayed stats change.</summary>
    [Id(8)]
    public ImmutableArray<CarriedStatus> Statuses { get; init; } = [];

    /// <summary>Les recharges emportées hors de la dernière zone, décomptées du temps passé dehors ;
    /// données une fois (M1d §2.3).</summary>
    [Id(9)]
    public ImmutableArray<CarriedCooldown> Cooldowns { get; init; } = [];

    /// <summary>Le GCD qui restait en quittant la dernière zone, décompté de même.</summary>
    [Id(10)]
    public int GlobalCooldownRemainingMs { get; init; }
}
