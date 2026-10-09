using Vortex.Rpg.Content.Definitions;

namespace Vortex.Rpg.Skills.Statuses;

/// <summary>A status on a fighter (spec M1b §7): who put it, until when, its stacks and its amount
/// per pulse or left to absorb, worked out with its caster's power when it was put.</summary>
public sealed class ActiveStatus
{
    public required StatusDefinition Definition { get; init; }

    public required int CasterObjectId { get; init; }

    public required long EndsAtMs { get; set; }

    /// <summary>Dot and hot only.</summary>
    public long NextPulseAtMs { get; set; }

    public int Stacks { get; set; } = 1;

    public double Amount { get; set; }

    /// <summary>The order it was put in, for the oldest to go first and the newest to be dispelled
    /// first.</summary>
    public required long Sequence { get; init; }
}
