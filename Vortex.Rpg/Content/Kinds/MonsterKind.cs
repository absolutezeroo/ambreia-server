using System.Collections.Generic;
using System.Linq;
using Vortex.Primitives.Rpg.Content;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Content.Validation;

namespace Vortex.Rpg.Content.Kinds;

/// <summary>Monsters (spec M1a §5.1). The behaviour key is checked by the module that runs monsters,
/// which knows the declared ones.</summary>
public sealed class MonsterKind : ContentKind<MonsterDefinition>
{
    private const int MinAttackIntervalMs = 500;
    private const int MaxCorpseSeconds = 10;

    public override string Kind => "monster";

    protected override IEnumerable<ContentFinding> Check(MonsterDefinition m, ContentCheck check)
    {
        MonsterStatBlock b = m.Base;
        MonsterStatBlock l = m.PerLevel;

        int[] values =
        [
            b.Health,
            b.Armor,
            b.DamageMin,
            b.DamageMax,
            l.Health,
            l.Armor,
            l.DamageMin,
            l.DamageMax,
            m.LeashRadius,
            m.Xp,
            m.XpPerLevel,
        ];

        if (values.Any(v => v < 0))
        {
            yield return Error("NegativeValue", m.Id, "Stats, leash and XP are never negative.");
        }

        if (b.Health < 1)
        {
            yield return Error("NoHealth", m.Id, "A monster has at least 1 health at level 1.");
        }

        if (b.DamageMin > b.DamageMax || l.DamageMin > l.DamageMax)
        {
            yield return Error("DamageRange", m.Id, "Minimum damage is never above maximum.");
        }

        if (m.AttackRange < 1)
        {
            yield return Error("BadRange", m.Id, $"Attack range {m.AttackRange}: at least 1.");
        }

        if (m.AttackIntervalMs < MinAttackIntervalMs)
        {
            yield return Error(
                "BadInterval",
                m.Id,
                $"Attack interval {m.AttackIntervalMs} ms: at least {MinAttackIntervalMs}."
            );
        }

        if (m.CorpseSeconds is < 0 or > MaxCorpseSeconds)
        {
            yield return Error(
                "BadCorpse",
                m.Id,
                $"Corpse {m.CorpseSeconds} s: between 0 and {MaxCorpseSeconds}."
            );
        }

        double[] percents = [b.CritPercent, b.DodgePercent, l.CritPercent, l.DodgePercent];

        if (percents.Any(p => p is < 0 or > 100))
        {
            yield return Error("BadPercent", m.Id, "Crit and dodge are between 0 and 100 %.");
        }

        if (!IsPetFigure(m.Figure))
        {
            yield return Error(
                "BadFigure",
                m.Id,
                $"Figure '{m.Figure}': a Habbo pet is \"<type> <race> <colour>\"."
            );
        }
    }

    private static bool IsPetFigure(string figure)
    {
        string[] parts = figure.Split(' ');

        return parts.Length == 3
            && int.TryParse(parts[0], out _)
            && int.TryParse(parts[1], out _)
            && parts[2].Length > 0;
    }
}
