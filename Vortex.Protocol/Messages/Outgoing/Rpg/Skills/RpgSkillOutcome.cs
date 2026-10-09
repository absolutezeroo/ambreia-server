using Orleans;

namespace Vortex.Protocol.Messages.Outgoing.Rpg.Skills;

/// <summary>What a skill or a status did to one fighter: damage, a dodge or a heal, and the
/// target's health after it.</summary>
[GenerateSerializer, Immutable]
public sealed record RpgSkillOutcome
{
    public const string Hit = "hit";
    public const string Crit = "crit";
    public const string Dodge = "dodge";
    public const string Heal = "heal";

    [Id(0)]
    public required int SourceObjectId { get; init; }

    /// <summary>The skill or status id.</summary>
    [Id(1)]
    public required string SourceId { get; init; }

    [Id(2)]
    public required int TargetObjectId { get; init; }

    /// <summary>Damage taken after any shield, or health restored.</summary>
    [Id(3)]
    public required int Amount { get; init; }

    /// <summary>Damage a shield took instead.</summary>
    [Id(4)]
    public required int Absorbed { get; init; }

    [Id(5)]
    public required string Outcome { get; init; }

    [Id(6)]
    public required int Health { get; init; }

    [Id(7)]
    public required int MaxHealth { get; init; }

    [Id(8)]
    public required bool Killed { get; init; }
}
