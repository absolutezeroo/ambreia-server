using System.Collections.Generic;
using System.Threading.Tasks;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Content.Rules;
using Vortex.Rpg.Content.Validation;

namespace Vortex.Rpg.Quests.Rewards;

/// <summary>
/// XP (spec M2a §3.3): through the character's gain, as a kill gives it — the operator grant has a
/// daily cap a quest must not hit. At most once: the log is written done before this runs.
/// </summary>
public sealed class XpReward : IQuestRewardKind
{
    public string Kind => "xp";

    public async Task GrantAsync(QuestRewardDefinition reward, QuestGrant grant) =>
        await grant.Character.GainXpAsync(
            QuestFields.Read<XpFields>(reward.Fields).Amount,
            $"quest:{grant.QuestId}"
        );

    public IEnumerable<string> Problems(QuestRewardDefinition reward, ContentCheck check)
    {
        if (QuestFields.Problem<XpFields>(reward.Fields) is { } problem)
        {
            yield return problem;
        }
        else if (QuestFields.Read<XpFields>(reward.Fields).Amount < 1)
        {
            yield return "amount is at least 1.";
        }
    }
}
