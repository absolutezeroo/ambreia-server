using System.Collections.Generic;
using System.Linq;
using Vortex.Primitives.Rpg.Content;
using Vortex.Primitives.World.Rules;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Content.Rules;
using Vortex.Rpg.Content.Validation;

namespace Vortex.Rpg.Content.Kinds;

/// <summary>Quests (spec M2a §2.1, §2.3): public, the journal reads them; each piece checked by its own kind.</summary>
public sealed class QuestKind(
    IEnumerable<IQuestObjectiveRule> objectives,
    IEnumerable<IQuestRewardRule> rewards
) : ContentKind<QuestDefinition>
{
    private readonly Dictionary<string, IQuestObjectiveRule> _objectives = objectives.ToDictionary(
        r => r.Kind
    );
    private readonly Dictionary<string, IQuestRewardRule> _rewards = rewards.ToDictionary(r =>
        r.Kind
    );

    public override string Kind => "quest";

    public override bool Public => true;

    protected override IEnumerable<string> TextKeys(QuestDefinition q) =>
        q.Objectives.Select((o, i) => o.Text ?? $"{q.Id}.objective.{i}");

    protected override IEnumerable<ContentFinding> Check(QuestDefinition q, ContentCheck check)
    {
        if (q.Objectives.Count == 0)
        {
            yield return Error("NoObjective", q.Id, "A quest asks for at least one thing.");
        }

        for (int i = 0; i < q.Objectives.Count; i++)
        {
            QuestObjectiveDefinition o = q.Objectives[i];

            if (!_objectives.TryGetValue(o.Kind, out IQuestObjectiveRule? rule))
            {
                yield return Error(
                    "UnknownObjective",
                    q.Id,
                    $"objectives[{i}]: no module runs the kind '{o.Kind}'."
                );

                continue;
            }

            if (o.Count < 1)
            {
                yield return Error(
                    "BadCount",
                    q.Id,
                    $"objectives[{i}]: count {o.Count}, at least 1."
                );
            }

            foreach (string problem in rule.Problems(o, check))
            {
                yield return Error("BadObjective", q.Id, $"objectives[{i}] ({o.Kind}): {problem}");
            }
        }

        for (int i = 0; i < q.Rewards.Count; i++)
        {
            QuestRewardDefinition r = q.Rewards[i];

            if (!_rewards.TryGetValue(r.Kind, out IQuestRewardRule? rule))
            {
                yield return Error(
                    "UnknownReward",
                    q.Id,
                    $"rewards[{i}]: no module grants the kind '{r.Kind}'."
                );

                continue;
            }

            foreach (string problem in rule.Problems(r, check))
            {
                yield return Error("BadReward", q.Id, $"rewards[{i}] ({r.Kind}): {problem}");
            }
        }

        foreach (QuestStateCondition required in q.Requires.OfType<QuestStateCondition>())
        {
            if (!check.Is<QuestDefinition>(required.Quest))
            {
                yield return Error(
                    "BrokenReference",
                    q.Id,
                    $"requires: no quest '{required.Quest}'."
                );
            }
        }
    }
}
