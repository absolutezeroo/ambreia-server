using System.Collections.Generic;
using System.Linq;
using Vortex.Primitives.Rpg.Content;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Content.Validation;

namespace Vortex.Rpg.Content.Kinds;

/// <summary>Status effects (spec M1b §5.3).</summary>
public sealed class StatusKind : ContentKind<StatusDefinition>
{
    private const int MaxSlowPercent = 90;

    public override string Kind => "status";

    public override bool Public => true;

    protected override IEnumerable<ContentFinding> Check(StatusDefinition s, ContentCheck check)
    {
        foreach (string problem in VfxCheck.Problems(s.Vfx))
        {
            yield return Error("BadVfx", s.Id, $"vfx: {problem}");
        }

        if (s.DurationMs <= 0)
        {
            yield return Error("BadDuration", s.Id, "A status lasts more than 0 ms.");
        }

        double[] values = [s.PeriodMs, s.MaxStacks, s.Base, s.Coefficient, s.SlowPercent];

        if (values.Any(v => v < 0))
        {
            yield return Error(
                "NegativeValue",
                s.Id,
                "Period, stacks, amounts and slow are never negative."
            );
        }

        if (s.Type is StatusType.Dot or StatusType.Hot && s.PeriodMs <= 0)
        {
            yield return Error(
                "PeriodicWithoutPeriod",
                s.Id,
                $"A {s.Type} needs periodMs above 0."
            );
        }

        if (s.Stacking == StatusStacking.Stack && s.MaxStacks < 2)
        {
            yield return Error(
                "StackWithoutMax",
                s.Id,
                "A stacking status needs maxStacks of 2 or more."
            );
        }

        if (s.Type == StatusType.Slow && s.SlowPercent is < 1 or > MaxSlowPercent)
        {
            yield return Error("BadSlow", s.Id, $"A slow is between 1 and {MaxSlowPercent} %.");
        }

        if (s.Type == StatusType.Shield && s.Base <= 0 && s.Coefficient <= 0)
        {
            yield return Error(
                "ShieldWithoutAmount",
                s.Id,
                "A shield needs a base or a coefficient."
            );
        }

        if (s.Type is StatusType.Dot or StatusType.Hot && s.Base <= 0 && s.Coefficient <= 0)
        {
            yield return Error(
                "PeriodicWithoutAmount",
                s.Id,
                $"A {s.Type} needs a base or a coefficient: it would pulse for nothing."
            );
        }

        if (s.Type is StatusType.Buff or StatusType.Debuff && s.Modifiers.Count == 0)
        {
            yield return Error("NoModifier", s.Id, $"A {s.Type} changes at least one stat.");
        }

        // "modifiers":[null] parses: the JSON options do not look inside a list.
        if (s.Modifiers.Any(m => m is null))
        {
            yield return Error("NullEntry", s.Id, "A modifier is null.");
        }
    }
}
