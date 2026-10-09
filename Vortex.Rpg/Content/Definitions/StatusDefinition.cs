using System.Collections.Generic;

namespace Vortex.Rpg.Content.Definitions;

/// <summary>A status effect (spec M1b §5.3): what it does, how long, how it stacks.</summary>
public sealed record StatusDefinition : ContentDefinition
{
    public required StatusType Type { get; init; }

    public required int DurationMs { get; init; }

    /// <summary>Required for <see cref="StatusType.Dot"/> and <see cref="StatusType.Hot"/>.</summary>
    public int PeriodMs { get; init; }

    public StatusStacking Stacking { get; init; } = StatusStacking.Refresh;

    /// <summary>Required (at least 2) for <see cref="StatusStacking.Stack"/>.</summary>
    public int MaxStacks { get; init; }

    public bool Dispellable { get; init; } = true;

    /// <summary>Survives leaving the zone (spec M1b §7.4).</summary>
    public bool Persistent { get; init; }

    /// <summary>Buffs and debuffs.</summary>
    public List<StatModifierDefinition> Modifiers { get; init; } = [];

    /// <summary>Per pulse (dot, hot) or absorbed (shield), before the caster's power.</summary>
    public double Base { get; init; }

    /// <summary>Times the caster's power, taken when the status is applied.</summary>
    public double Coefficient { get; init; }

    /// <summary>Required (1–90) for <see cref="StatusType.Slow"/>.</summary>
    public int SlowPercent { get; init; }
}
