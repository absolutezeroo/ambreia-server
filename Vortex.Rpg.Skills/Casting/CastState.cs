using Vortex.Rpg.Content.Definitions;

namespace Vortex.Rpg.Skills.Casting;

/// <summary>A cast or a channel under way (spec M1b §6.3), a part of its caster.</summary>
public sealed class CastState
{
    public required SkillDefinition Skill { get; init; }

    public required int TargetObjectId { get; init; }

    public required (int X, int Y)? Cell { get; init; }

    public required long EndsAtMs { get; init; }

    /// <summary>Channels only.</summary>
    public long NextPulseAtMs { get; set; }

    public required bool Channel { get; init; }
}
