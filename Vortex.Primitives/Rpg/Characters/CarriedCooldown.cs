using Orleans;

namespace Vortex.Primitives.Rpg.Characters;

/// <summary>Une recharge qu'un joueur emporte hors d'une zone (M1d §2.3), avec le temps qui reste.</summary>
[GenerateSerializer, Immutable]
public sealed record CarriedCooldown
{
    [Id(0)]
    public required string SkillId { get; init; }

    [Id(1)]
    public required int RemainingMs { get; init; }
}
