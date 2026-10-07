using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Vortex.Dashboard.API.Api.Progression.Contracts;
using Vortex.Database.Context;
using Vortex.Database.Entities.Quests;
using Vortex.Primitives.Quests;

namespace Vortex.Dashboard.API.Api.Progression;

/// <summary>
/// Read surface for the content behind the quest system that is not a quest: daily-task
/// definitions with their rewards and take-up. Authoring
/// lives in <see cref="Operations.QuestContentOperations"/>.
/// </summary>
/// <remarks>
/// One dependency: a context. This subject reads and shapes rows and needs nothing else, which was
/// invisible while it was one file of a class that injected ten.
/// </remarks>
internal sealed class QuestContentReads(IDbContextFactory<VortexDbContext> dbContextFactory)
    : DashboardReads(dbContextFactory)
{
    /// <summary>
    /// Daily-task definitions with their rewards and how they have actually landed: how many
    /// assignments each has produced, and how many were finished and claimed. A task nobody ever
    /// completes is the one worth re-tuning.
    /// </summary>
    public Task<DailyTaskList> DailyTasksAsync(CancellationToken ct) =>
        QueryAsync<DailyTaskList>(
            async db =>
            {
                List<DailyTaskEntity> tasks = await db
                    .DailyTasks.AsNoTracking()
                    .OrderBy(t => t.IsBonus)
                    .ThenBy(t => t.SortOrder)
                    .ThenBy(t => t.Id)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                if (tasks.Count == 0)
                {
                    return new DailyTaskList(0, [], QuestTypeNames());
                }

                List<int> taskIds = [.. tasks.Select(t => t.Id)];

                ILookup<int, DailyTaskRewardEntity> rewardsByTask = (
                    await db
                        .DailyTaskRewards.AsNoTracking()
                        .Where(r => taskIds.Contains(r.DailyTaskEntityId))
                        .OrderBy(r => r.Id)
                        .ToListAsync(ct)
                        .ConfigureAwait(false)
                ).ToLookup(r => r.DailyTaskEntityId);

                Dictionary<int, (int Assigned, int Completed, int Claimed)> stats = (
                    await db
                        .PlayerDailyTasks.AsNoTracking()
                        .Where(a => taskIds.Contains(a.DailyTaskEntityId))
                        .GroupBy(a => a.DailyTaskEntityId)
                        .Select(g => new
                        {
                            TaskId = g.Key,
                            Assigned = g.Count(),
                            Completed = g.Count(a => a.Status != DailyTaskStatus.Available),
                            Claimed = g.Count(a => a.Status == DailyTaskStatus.Claimed),
                        })
                        .ToListAsync(ct)
                        .ConfigureAwait(false)
                ).ToDictionary(x => x.TaskId, x => (x.Assigned, x.Completed, x.Claimed));

                List<DailyTaskRow> items = tasks
                    .Select(task =>
                    {
                        (int assigned, int completed, int claimed) = stats.GetValueOrDefault(
                            task.Id
                        );

                        return new DailyTaskRow(
                            task.Id,
                            task.TaskCode,
                            task.QuestTypeCode,
                            task.IsBonus,
                            task.ImageVersion,
                            task.CatalogName,
                            task.RequiredRepeats,
                            task.Enabled,
                            task.SortOrder,
                            assigned,
                            completed,
                            claimed,
                            assigned <= 0 ? 0 : Math.Round(completed * 100d / assigned, 1),
                            rewardsByTask[task.Id]
                                .Select(r => new DailyTaskRewardRow(
                                    r.Id,
                                    r.ProductItemTypeId,
                                    r.RewardTypeId,
                                    r.ExtraParams,
                                    r.Amount
                                ))
                                .ToList()
                        );
                    })
                    .ToList();

                return new DailyTaskList(items.Count, items, QuestTypeNames());
            },
            ct
        );

    /// <summary>
    /// The objective vocabulary daily tasks share with quests, read by reflection so the picker can
    /// never offer a type the progression code does not know.
    /// </summary>
    private static List<string> QuestTypeNames() =>
        [
            .. typeof(QuestTypes)
                .GetFields(
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static
                )
                .Where(f => f.IsLiteral && f.FieldType == typeof(string))
                .Select(f => (string)f.GetRawConstantValue()!)
                .Where(name => !name.StartsWith("offer_", StringComparison.Ordinal))
                .OrderBy(name => name, StringComparer.Ordinal),
        ];
}
