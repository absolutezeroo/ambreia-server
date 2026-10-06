using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orleans;
using Vortex.Dashboard.API.Admin.Rules;
using Vortex.Database.Context;
using Vortex.Database.Entities.Quests;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players.Providers;
using Vortex.Primitives.Players.Wallet;
using Vortex.Primitives.Quests;
using Vortex.Primitives.Quests.Admin;

namespace Vortex.Dashboard.API.Admin.Progression;

/// <summary>
/// CRUD for community goals and daily-task definitions. A plain singleton opening a short-lived
/// context per call, like the quest and poll admin services: this data is not grain-owned and admin
/// writes are rare. Community-goal writes reload the kept-alive goal grain, which caches the active
/// goal and the hotel total — without that a retimed ladder would only take effect on restart.
/// </summary>
internal sealed class QuestContentAdminService(
    IDbContextFactory<VortexDbContext> dbContextFactory,
    IGrainFactory grainFactory,
    ICurrencyTypeProvider currencyTypes,
    ILogger<QuestContentAdminService> logger
) : IQuestContentAdminService
{
    public async Task<QuestContentAdminResult> CreateCommunityGoalAsync(
        CommunityGoalSpec spec,
        CancellationToken ct
    )
    {
        if (QuestContentRules.ValidateGoal(spec) is { } error)
        {
            return QuestContentAdminResult.Fail(error);
        }

        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        string code = spec.Code.Trim();

        if (await db.CommunityGoals.AnyAsync(g => g.Code == code, ct).ConfigureAwait(false))
        {
            return QuestContentAdminResult.Fail("goal_code_taken");
        }

        CommunityGoalEntity entity = new()
        {
            Code = code,
            CampaignCode = (spec.CampaignCode ?? string.Empty).Trim(),
            ScorePerQuest = Math.Max(1, spec.ScorePerQuest),
            Enabled = spec.Enabled,
            EndsAt = spec.EndsAt,
            SortOrder = spec.SortOrder,
        };

        db.CommunityGoals.Add(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        AddLevels(db, entity.Id, spec.Levels);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadGoalAsync(ct).ConfigureAwait(false);

        return QuestContentAdminResult.Ok(entity.Id);
    }

    public async Task<QuestContentAdminResult> UpdateCommunityGoalAsync(
        int goalId,
        CommunityGoalSpec spec,
        CancellationToken ct
    )
    {
        if (QuestContentRules.ValidateGoal(spec) is { } error)
        {
            return QuestContentAdminResult.Fail(error);
        }

        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        CommunityGoalEntity? entity = await db
            .CommunityGoals.FirstOrDefaultAsync(g => g.Id == goalId, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return QuestContentAdminResult.Fail("goal_not_found");
        }

        string code = spec.Code.Trim();

        if (
            await db
                .CommunityGoals.AnyAsync(g => g.Code == code && g.Id != goalId, ct)
                .ConfigureAwait(false)
        )
        {
            return QuestContentAdminResult.Fail("goal_code_taken");
        }

        entity.Code = code;
        entity.CampaignCode = (spec.CampaignCode ?? string.Empty).Trim();
        entity.ScorePerQuest = Math.Max(1, spec.ScorePerQuest);
        entity.Enabled = spec.Enabled;
        entity.EndsAt = spec.EndsAt;
        entity.SortOrder = spec.SortOrder;

        // The ladder is replaced wholesale: rungs hold no player state, so nothing is lost, and
        // reshaping a goal rung by rung would otherwise mean a write per level.
        List<CommunityGoalLevelEntity> existing = await db
            .CommunityGoalLevels.Where(l => l.CommunityGoalEntityId == goalId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        db.CommunityGoalLevels.RemoveRange(existing);
        AddLevels(db, goalId, spec.Levels);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadGoalAsync(ct).ConfigureAwait(false);

        return QuestContentAdminResult.Ok(goalId);
    }

    public async Task<QuestContentAdminResult> DeleteCommunityGoalAsync(
        int goalId,
        CancellationToken ct
    )
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        CommunityGoalEntity? entity = await db
            .CommunityGoals.FirstOrDefaultAsync(g => g.Id == goalId, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return QuestContentAdminResult.Fail("goal_not_found");
        }

        // Contributions reference the goal non-cascade on purpose: what a hotel built together is a
        // record, not a draft. Disabling is the way to retire one.
        bool hasContributions = await db
            .PlayerCommunityGoalContributions.AnyAsync(c => c.CommunityGoalEntityId == goalId, ct)
            .ConfigureAwait(false);

        if (hasContributions)
        {
            return QuestContentAdminResult.Fail("goal_has_contributions");
        }

        db.CommunityGoals.Remove(entity);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReloadGoalAsync(ct).ConfigureAwait(false);

        return QuestContentAdminResult.Ok(goalId);
    }

    public async Task<QuestContentAdminResult> CreateDailyTaskAsync(
        DailyTaskSpec spec,
        CancellationToken ct
    )
    {
        if (
            (QuestContentRules.ValidateDailyTask(spec) ?? ValidateRewardCurrencies(spec)) is
            { } error
        )
        {
            return QuestContentAdminResult.Fail(error);
        }

        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        string taskCode = spec.TaskCode.Trim();

        if (await db.DailyTasks.AnyAsync(t => t.TaskCode == taskCode, ct).ConfigureAwait(false))
        {
            return QuestContentAdminResult.Fail("task_code_taken");
        }

        DailyTaskEntity entity = new()
        {
            TaskCode = taskCode,
            QuestTypeCode = spec.QuestTypeCode.Trim(),
            IsBonus = spec.IsBonus,
            ImageVersion = (spec.ImageVersion ?? string.Empty).Trim(),
            CatalogName = (spec.CatalogName ?? string.Empty).Trim(),
            RequiredRepeats = Math.Max(1, spec.RequiredRepeats),
            Enabled = spec.Enabled,
            SortOrder = spec.SortOrder,
        };

        db.DailyTasks.Add(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        AddRewards(db, entity.Id, spec.Rewards);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // No cache to reload: daily-task definitions are read per request by the player grain, not
        // held by a kept-alive one.
        return QuestContentAdminResult.Ok(entity.Id);
    }

    public async Task<QuestContentAdminResult> UpdateDailyTaskAsync(
        int taskId,
        DailyTaskSpec spec,
        CancellationToken ct
    )
    {
        if (
            (QuestContentRules.ValidateDailyTask(spec) ?? ValidateRewardCurrencies(spec)) is
            { } error
        )
        {
            return QuestContentAdminResult.Fail(error);
        }

        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        DailyTaskEntity? entity = await db
            .DailyTasks.FirstOrDefaultAsync(t => t.Id == taskId, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return QuestContentAdminResult.Fail("task_not_found");
        }

        string taskCode = spec.TaskCode.Trim();

        if (
            await db
                .DailyTasks.AnyAsync(t => t.TaskCode == taskCode && t.Id != taskId, ct)
                .ConfigureAwait(false)
        )
        {
            return QuestContentAdminResult.Fail("task_code_taken");
        }

        entity.TaskCode = taskCode;
        entity.QuestTypeCode = spec.QuestTypeCode.Trim();
        entity.IsBonus = spec.IsBonus;
        entity.ImageVersion = (spec.ImageVersion ?? string.Empty).Trim();
        entity.CatalogName = (spec.CatalogName ?? string.Empty).Trim();
        entity.RequiredRepeats = Math.Max(1, spec.RequiredRepeats);
        entity.Enabled = spec.Enabled;
        entity.SortOrder = spec.SortOrder;

        List<DailyTaskRewardEntity> existing = await db
            .DailyTaskRewards.Where(r => r.DailyTaskEntityId == taskId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        db.DailyTaskRewards.RemoveRange(existing);
        AddRewards(db, taskId, spec.Rewards);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return QuestContentAdminResult.Ok(taskId);
    }

    public async Task<QuestContentAdminResult> DeleteDailyTaskAsync(
        int taskId,
        CancellationToken ct
    )
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        DailyTaskEntity? entity = await db
            .DailyTasks.FirstOrDefaultAsync(t => t.Id == taskId, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return QuestContentAdminResult.Fail("task_not_found");
        }

        // Assignments reference the definition non-cascade, so a task anyone has ever been given is
        // disabled rather than deleted -- otherwise today's boards would break mid-day.
        bool hasAssignments = await db
            .PlayerDailyTasks.AnyAsync(a => a.DailyTaskEntityId == taskId, ct)
            .ConfigureAwait(false);

        if (hasAssignments)
        {
            return QuestContentAdminResult.Fail("task_has_assignments");
        }

        List<DailyTaskRewardEntity> rewards = await db
            .DailyTaskRewards.Where(r => r.DailyTaskEntityId == taskId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        db.DailyTaskRewards.RemoveRange(rewards);
        db.DailyTasks.Remove(entity);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return QuestContentAdminResult.Ok(taskId);
    }

    private static void AddLevels(
        VortexDbContext db,
        int goalId,
        IReadOnlyList<CommunityGoalLevelSpec> levels
    )
    {
        int number = 1;

        // Renumbered in threshold order: the client pairs reward limits with levels by position, so a
        // ladder numbered out of order would hand out the wrong rung's limits.
        foreach (CommunityGoalLevelSpec level in levels.OrderBy(l => l.ScoreThreshold))
        {
            db.CommunityGoalLevels.Add(
                new CommunityGoalLevelEntity
                {
                    CommunityGoalEntityId = goalId,
                    LevelNumber = number,
                    ScoreThreshold = Math.Max(0, level.ScoreThreshold),
                    RewardUserLimit = Math.Max(0, level.RewardUserLimit),
                }
            );

            number++;
        }
    }

    private static void AddRewards(
        VortexDbContext db,
        int taskId,
        IReadOnlyList<DailyTaskRewardSpec> rewards
    )
    {
        foreach (DailyTaskRewardSpec reward in rewards)
        {
            db.DailyTaskRewards.Add(
                new DailyTaskRewardEntity
                {
                    DailyTaskEntityId = taskId,
                    ProductItemTypeId = reward.ProductItemTypeId,
                    RewardTypeId = reward.RewardTypeId.Trim(),
                    ExtraParams = (reward.ExtraParams ?? string.Empty).Trim(),
                    Amount = Math.Max(0, reward.Amount),
                }
            );
        }
    }

    private async Task ReloadGoalAsync(CancellationToken ct)
    {
        try
        {
            await grainFactory.GetCommunityGoalGrain().ReloadAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The DB write already committed -- the live goal is now stale until the next reload or
            // restart. Never swallow this: it is the "DB write not reflected in live state" bug
            // class called out in AGENTS.md.
            logger.LogError(
                ex,
                "Community goal reload failed after an admin write committed -- the live goal is now stale until the next reload or restart"
            );

            throw;
        }
    }

    /// <summary>
    /// Null when every reward this task promises would really be handed over.
    /// </summary>
    /// <remarks>
    /// A reward type that names neither credits nor an activity-point number is an item code, and
    /// nothing grants items on a daily task yet — the grain logs it and hands over nothing. This
    /// used to let it through on the grounds that the grain reported it, which made the board a
    /// place where a reward could be promised to a player that no code path could pay. Refusing it
    /// here is the difference between a promise that is late and one that was never keepable.
    ///
    /// The check is on the way in, so rows configured before it stay as they are; editing one now
    /// surfaces it, which is the point. Delete this branch when items are wired, not before.
    /// </remarks>
    private string? ValidateRewardCurrencies(DailyTaskSpec spec)
    {
        foreach (DailyTaskRewardSpec reward in spec.Rewards)
        {
            if (
                CurrencyRewardRules.ValidateNamed(
                    currencyTypes,
                    reward.RewardTypeId,
                    reward.Amount
                ) is
                { } error
            )
            {
                return error;
            }

            if (!CurrencyRewardRules.TryParseNamed(reward.RewardTypeId, out _))
            {
                return $"Reward '{reward.RewardTypeId}' is not a currency, and daily tasks cannot "
                    + "hand over items yet. The task would promise it and pay nothing.";
            }
        }

        return null;
    }
}
