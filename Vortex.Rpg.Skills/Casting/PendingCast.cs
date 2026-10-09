using Vortex.Rpg.Content.Definitions;

namespace Vortex.Rpg.Skills.Casting;

/// <summary>A cast waiting (spec M1b §6.2–§6.3): the caster walks to reach its target, or an instant
/// already paid lands at the next tick. A part of its caster.</summary>
public sealed class PendingCast
{
    public required SkillDefinition Skill { get; init; }

    public required int TargetObjectId { get; init; }

    public required (int X, int Y)? Cell { get; init; }

    /// <summary>An instant accepted and paid: it lands at the next tick.</summary>
    public bool Ready { get; set; }

    /// <summary>Where the approach walks to; it walks again when that moves.</summary>
    public (int X, int Y)? Goal { get; set; }
}
