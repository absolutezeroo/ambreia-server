using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Quests.Admin;

namespace Vortex.Primitives.Quests;

/// <summary>
/// CRUD for the content behind the quest system that is not a quest: daily-task definitions with
/// their rewards.
/// </summary>
public interface IQuestContentAdminService
{
    Task<QuestContentAdminResult> CreateDailyTaskAsync(DailyTaskSpec spec, CancellationToken ct);

    Task<QuestContentAdminResult> UpdateDailyTaskAsync(
        int taskId,
        DailyTaskSpec spec,
        CancellationToken ct
    );

    Task<QuestContentAdminResult> DeleteDailyTaskAsync(int taskId, CancellationToken ct);
}
