using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Dashboard.API.Operations.Progression.Contracts;
using Vortex.Primitives.Quests;
using Vortex.Primitives.Quests.Admin;

namespace Vortex.Dashboard.API.Operations.Progression;

/// <summary>
/// Community goal and daily-task admin operations.
/// </summary>
/// <remarks>
/// Two dependencies: the runner that audits every write, and the domain's own quest-content
/// authoring service. Never a direct DB write — the same contract the catalogue and poll operations
/// follow.
/// </remarks>
internal sealed class QuestContentOperations(
    OperationRunner runner,
    IQuestContentAdminService questContentAdmin
)
{
    private readonly OperationRunner _runner = runner;
    private readonly IQuestContentAdminService _questContentAdmin = questContentAdmin;

    public Task<OperationResult> CreateCommunityGoalAsync(
        CreateCommunityGoalRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.community_goal.create",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new
            {
                request.Code,
                request.CampaignCode,
                levelCount = request.Levels?.Count ?? 0,
            },
            work: async c =>
                Throw(
                    await _questContentAdmin
                        .CreateCommunityGoalAsync(ToSpec(request), c)
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> UpdateCommunityGoalAsync(
        UpdateCommunityGoalRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.community_goal.update",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new
            {
                request.GoalId,
                request.Code,
                request.Enabled,
                levelCount = request.Levels?.Count ?? 0,
            },
            work: async c =>
                Throw(
                    await _questContentAdmin
                        .UpdateCommunityGoalAsync(request.GoalId, ToSpec(request), c)
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> DeleteCommunityGoalAsync(
        DeleteCommunityGoalRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.community_goal.delete",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { request.GoalId },
            work: async c =>
                Throw(
                    await _questContentAdmin
                        .DeleteCommunityGoalAsync(request.GoalId, c)
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> CreateDailyTaskAsync(
        CreateDailyTaskRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.daily_task.create",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new
            {
                request.TaskCode,
                request.QuestTypeCode,
                request.IsBonus,
            },
            work: async c =>
                Throw(
                    await _questContentAdmin
                        .CreateDailyTaskAsync(ToSpec(request), c)
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> UpdateDailyTaskAsync(
        UpdateDailyTaskRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.daily_task.update",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new
            {
                request.TaskId,
                request.TaskCode,
                request.Enabled,
            },
            work: async c =>
                Throw(
                    await _questContentAdmin
                        .UpdateDailyTaskAsync(request.TaskId, ToSpec(request), c)
                        .ConfigureAwait(false)
                ),
            ct
        );

    public Task<OperationResult> DeleteDailyTaskAsync(
        DeleteDailyTaskRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.daily_task.delete",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { request.TaskId },
            work: async c =>
                Throw(
                    await _questContentAdmin
                        .DeleteDailyTaskAsync(request.TaskId, c)
                        .ConfigureAwait(false)
                ),
            ct
        );

    /// <summary>
    /// Turns a refused admin write into the exception <c>ExecuteAsync</c> records and reports, so
    /// every operation here reads as one expression instead of repeating the same if/throw.
    /// </summary>
    private static void Throw(QuestContentAdminResult result)
    {
        if (!result.Success)
        {
            throw new InvalidOperationException(result.ErrorCode);
        }
    }

    private static CommunityGoalSpec ToSpec(CreateCommunityGoalRequest request) =>
        new(
            request.Code,
            request.CampaignCode,
            request.ScorePerQuest,
            request.Enabled,
            request.EndsAt,
            request.SortOrder,
            ToLevels(request.Levels)
        );

    private static CommunityGoalSpec ToSpec(UpdateCommunityGoalRequest request) =>
        new(
            request.Code,
            request.CampaignCode,
            request.ScorePerQuest,
            request.Enabled,
            request.EndsAt,
            request.SortOrder,
            ToLevels(request.Levels)
        );

    private static DailyTaskSpec ToSpec(CreateDailyTaskRequest request) =>
        new(
            request.TaskCode,
            request.QuestTypeCode,
            request.IsBonus,
            request.ImageVersion,
            request.CatalogName,
            request.RequiredRepeats,
            request.Enabled,
            request.SortOrder,
            ToRewards(request.Rewards)
        );

    private static DailyTaskSpec ToSpec(UpdateDailyTaskRequest request) =>
        new(
            request.TaskCode,
            request.QuestTypeCode,
            request.IsBonus,
            request.ImageVersion,
            request.CatalogName,
            request.RequiredRepeats,
            request.Enabled,
            request.SortOrder,
            ToRewards(request.Rewards)
        );

    private static IReadOnlyList<CommunityGoalLevelSpec> ToLevels(
        IReadOnlyList<CommunityGoalLevelBody>? levels
    ) =>
        levels is null
            ? []
            :
            [
                // The level number is derived from the threshold order by the admin service; the
                // placeholder here is never persisted.
                .. levels.Select(l => new CommunityGoalLevelSpec(
                    LevelNumber: 0,
                    l.ScoreThreshold,
                    l.RewardUserLimit
                )),
            ];

    private static IReadOnlyList<DailyTaskRewardSpec> ToRewards(
        IReadOnlyList<DailyTaskRewardBody>? rewards
    ) =>
        rewards is null
            ? []
            :
            [
                .. rewards.Select(r => new DailyTaskRewardSpec(
                    r.ProductItemTypeId,
                    r.RewardTypeId,
                    r.ExtraParams,
                    r.Amount
                )),
            ];
}
