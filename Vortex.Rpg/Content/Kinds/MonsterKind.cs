using System.Collections.Generic;
using System.Linq;
using Vortex.Primitives.Rpg.Content;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Content.Rules;
using Vortex.Rpg.Content.Validation;

namespace Vortex.Rpg.Content.Kinds;

/// <summary>Monsters (spec M1a §5.1, M1c §5.4): stats, a behaviour some module declares, skill rules
/// and phases.</summary>
public sealed class MonsterKind(IEnumerable<IMonsterBehaviorKey> behaviors)
    : ContentKind<MonsterDefinition>
{
    private const int MinAttackIntervalMs = 500;
    private const int MaxCorpseSeconds = 10;
    private const int MaxSummonCount = 8;

    private readonly HashSet<string> _behaviors = [.. behaviors.Select(b => b.Key)];

    public override string Kind => "monster";

    protected override IEnumerable<ContentFinding> Check(MonsterDefinition m, ContentCheck check)
    {
        foreach (ContentFinding finding in CheckStats(m))
        {
            yield return finding;
        }

        if (!_behaviors.Contains(m.Behavior))
        {
            yield return Error(
                "UnknownBehavior",
                m.Id,
                $"Behavior '{m.Behavior}' is not declared by any module."
            );
        }

        // "skills":[null] parses: the JSON options do not look inside a list.
        if (
            m.Skills.Any(r => r is null)
            || m.Phases.Any(p => p is null || p.Skills.Any(r => r is null))
        )
        {
            yield return Error("NullEntry", m.Id, "A skill rule or a phase is null.");

            yield break;
        }

        foreach (MonsterSkillRule rule in m.Skills.Concat(m.Phases.SelectMany(p => p.Skills)))
        {
            foreach (ContentFinding finding in CheckRule(m, rule, check))
            {
                yield return finding;
            }
        }

        foreach (ContentFinding finding in CheckPhases(m, check))
        {
            yield return finding;
        }

        if (
            m.Rank != MonsterRank.Normal
            && m.Skills.Count == 0
            && m.Phases.All(p => p.Skills.Count == 0)
        )
        {
            yield return Warning(
                "NoSkills",
                m.Id,
                $"A {m.Rank} with no skill fights like a common monster."
            );
        }
    }

    private static IEnumerable<ContentFinding> CheckRule(
        MonsterDefinition m,
        MonsterSkillRule rule,
        ContentCheck check
    )
    {
        if (check.Get<SkillDefinition>(rule.Skill) is not { } skill)
        {
            yield return Error("BrokenReference", m.Id, $"Skill '{rule.Skill}' does not exist.");

            yield break;
        }

        if (
            rule.Chance is < 1 or > 100
            || rule.BelowHealthPercent is < 1 or > 100
            || rule.MinRange < 0
            || rule.MinRange > skill.Range
        )
        {
            yield return Error(
                "BadRule",
                m.Id,
                $"Rule for '{rule.Skill}': chance and belowHealthPercent 1–100, minRange 0–{skill.Range}."
            );
        }
    }

    private static IEnumerable<ContentFinding> CheckPhases(MonsterDefinition m, ContentCheck check)
    {
        int maxLevel = check
            .All<ProgressionDefinition>()
            .Select(p => p.MaxLevel)
            .DefaultIfEmpty(int.MaxValue)
            .Max();

        if (m.Phases.GroupBy(p => p.AtHealthPercent).Any(g => g.Count() > 1))
        {
            yield return Error("BadPhase", m.Id, "Two phases share a threshold.");
        }

        foreach (MonsterPhase phase in m.Phases)
        {
            if (phase.AtHealthPercent is < 1 or > 99)
            {
                yield return Error(
                    "BadPhase",
                    m.Id,
                    $"Phase at {phase.AtHealthPercent} %: between 1 and 99."
                );
            }

            if (phase.Status is { } status && !check.Is<StatusDefinition>(status))
            {
                yield return Error("BrokenReference", m.Id, $"Status '{status}' does not exist.");
            }

            if (phase.Summon is not { } summon)
            {
                continue;
            }

            if (
                summon.Count is < 1 or > MaxSummonCount
                || summon.Level < 0
                || summon.Level > maxLevel
            )
            {
                yield return Error(
                    "BadPhase",
                    m.Id,
                    $"Summon of '{summon.Monster}': count 1–{MaxSummonCount}, level 0 (its own) to {maxLevel}."
                );
            }

            if (check.Get<MonsterDefinition>(summon.Monster) is not { } summoned)
            {
                yield return Error(
                    "BrokenReference",
                    m.Id,
                    $"Monster '{summon.Monster}' does not exist."
                );
            }
            else if (summoned.Rank == MonsterRank.Boss)
            {
                yield return Error(
                    "BossSummon",
                    m.Id,
                    $"'{summon.Monster}' is a boss: a boss is never summoned."
                );
            }
        }
    }

    private static IEnumerable<ContentFinding> CheckStats(MonsterDefinition m)
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
