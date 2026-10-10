using System.Collections.Generic;
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

    /// <summary>A telegraph's cells, fixed at the start (spec M1c §8): it lands on them alone.</summary>
    public IReadOnlyList<(int X, int Y)>? Cells { get; init; }

    /// <summary>Where a telegraph's targets are measured from, nearest first.</summary>
    public (int X, int Y) Origin { get; init; }
}
