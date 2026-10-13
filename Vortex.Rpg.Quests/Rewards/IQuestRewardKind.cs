using System.Threading.Tasks;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Content.Rules;

namespace Vortex.Rpg.Quests.Rewards;

/// <summary>A reward kind (spec M2a §3.4): its content rule and how it is given.</summary>
public interface IQuestRewardKind : IQuestRewardRule
{
    public Task GrantAsync(QuestRewardDefinition reward, QuestGrant grant);
}
