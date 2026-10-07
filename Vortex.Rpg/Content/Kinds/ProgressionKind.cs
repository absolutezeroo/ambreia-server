using System.Collections.Generic;
using System.Linq;
using Vortex.Primitives.Rpg.Content;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Content.Validation;

namespace Vortex.Rpg.Content.Kinds;

/// <summary>The XP curve and the derived-stat coefficients (spec Personnage §5).</summary>
public sealed class ProgressionKind : ContentKind<ProgressionDefinition>
{
    public override string Kind => "progression";

    protected override IEnumerable<ContentFinding> Check(
        ProgressionDefinition p,
        ContentCheck check
    )
    {
        if (p.MaxLevel < 2)
        {
            yield return Error("BadMaxLevel", p.Id, $"Max level {p.MaxLevel}: at least 2.");
        }

        if (p.XpToNext.Count != p.MaxLevel - 1)
        {
            yield return Error(
                "XpCurveLength",
                p.Id,
                $"xpToNext has {p.XpToNext.Count} values; {p.MaxLevel - 1} are needed (one per level up to {p.MaxLevel})."
            );
        }

        if (
            p.XpToNext.Any(xp => xp <= 0)
            || p.XpToNext.Zip(p.XpToNext.Skip(1)).Any(t => t.Second <= t.First)
        )
        {
            yield return Error(
                "XpCurveNotIncreasing",
                p.Id,
                "xpToNext values are above 0 and strictly increasing."
            );
        }

        if (p.HealthPerStrength < 0 || p.ManaPerIntelligence < 0)
        {
            yield return Error(
                "NegativeValue",
                p.Id,
                "Health and mana per point are never negative."
            );
        }

        double[] percents =
        [
            p.CritBasePercent,
            p.CritPerAgilityPercent,
            p.CritCapPercent,
            p.DodgePerAgilityPercent,
            p.DodgeCapPercent,
        ];

        if (percents.Any(v => v < 0) || p.CritCapPercent > 100 || p.DodgeCapPercent > 100)
        {
            yield return Error(
                "BadPercent",
                p.Id,
                "Percentages are never negative and caps never exceed 100."
            );
        }
    }
}
