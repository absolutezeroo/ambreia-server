using Orleans;

namespace Vortex.Protocol.Messages.Outgoing.Rpg.Combat;

/// <summary>One hit of a combat tick (spec M1a §9.2): who hit whom, how much, how it went, and the
/// target's health after it.</summary>
[GenerateSerializer, Immutable]
public sealed record RpgCombatHit
{
    public const string Hit = "hit";
    public const string Crit = "crit";
    public const string Miss = "miss";

    /// <summary>The attacker's room object id.</summary>
    [Id(0)]
    public required int AttackerId { get; init; }

    /// <summary>The target's room object id.</summary>
    [Id(1)]
    public required int TargetId { get; init; }

    /// <summary>0 for a miss.</summary>
    [Id(2)]
    public required int Amount { get; init; }

    /// <summary><see cref="Hit"/>, <see cref="Crit"/> or <see cref="Miss"/>.</summary>
    [Id(3)]
    public required string Outcome { get; init; }

    [Id(4)]
    public required int Health { get; init; }

    [Id(5)]
    public required int MaxHealth { get; init; }

    [Id(6)]
    public required bool Killed { get; init; }
}
