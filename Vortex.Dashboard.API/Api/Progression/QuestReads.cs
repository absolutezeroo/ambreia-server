using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Vortex.Dashboard.API.Api.Hotel.Contracts;
using Vortex.Dashboard.API.Api.Progression.Contracts;
using Vortex.Dashboard.API.Infrastructure;
using Vortex.Database.Context;
using Vortex.Database.Entities.Quests;

namespace Vortex.Dashboard.API.Api.Progression;

/// <summary>
/// Read + analytics surface for quests. The admin CRUD lives in
/// <see cref="Operations.QuestOperations"/>; here we only read. Completion analytics are
/// aggregated from the <c>player_quests</c> table (there is no separate quest-completion audit
/// trail), keyed on each completed row's <c>CompletedAt</c>.
/// </summary>
internal sealed class QuestReads(
    IDbContextFactory<VortexDbContext> dbContextFactory,
    DashboardAssetUrls assetUrls
) : DashboardReads(dbContextFactory)
{
    private readonly DashboardAssetUrls _assetUrls = assetUrls;

    // Objective types that have a live progression trigger wired (a handler calls ProgressAsync with
    // this type). The others in QuestTypes are defined but not yet fired, so the admin can still pick
    // them but they won't advance until a trigger exists -- surfaced via the "wired" flag.
    private static readonly HashSet<string> WiredQuestTypes = new(StringComparer.Ordinal)
    {
        Vortex.Primitives.Quests.QuestTypes.RoomEntry,
        Vortex.Primitives.Quests.QuestTypes.FriendListSize,
        Vortex.Primitives.Quests.QuestTypes.AvatarLooks,
        Vortex.Primitives.Quests.QuestTypes.Chat,
        Vortex.Primitives.Quests.QuestTypes.Wave,
        Vortex.Primitives.Quests.QuestTypes.Dance,
        Vortex.Primitives.Quests.QuestTypes.CatalogPurchase,
        Vortex.Primitives.Quests.QuestTypes.Login,
        Vortex.Primitives.Quests.QuestTypes.MottoChange,
        Vortex.Primitives.Quests.QuestTypes.TradeCompleted,
        Vortex.Primitives.Quests.QuestTypes.CreateGroup,
        Vortex.Primitives.Quests.QuestTypes.JoinGroup,
        Vortex.Primitives.Quests.QuestTypes.BuyMembership,
        Vortex.Primitives.Quests.QuestTypes.PlaceItem,
    };

    /// <summary>
    /// The canonical objective types (the <c>quest_type</c> values), read by reflection from
    /// <see cref="Vortex.Primitives.Quests.QuestTypes"/> so the admin picks a real objective instead
    /// of typing a free string. <c>wired</c> marks the ones that actually advance today; the quest's
    /// step count (<c>TotalSteps</c>) is the goal (e.g. RoomEntry + 200 = "visit 200 rooms").
    /// </summary>
    public QuestTypeOptions QuestTypeOptions()
    {
        List<QuestTypeOption> items = typeof(Vortex.Primitives.Quests.QuestTypes)
            .GetFields(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static
            )
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => new QuestTypeOption(name, WiredQuestTypes.Contains(name)))
            .ToList();

        return new QuestTypeOptions(items.Count, items);
    }

    /// <summary>Every quest with its reward config, timer, and lifetime accept/complete counts,
    /// ordered like the client sees them (sort order then id).</summary>
    public Task<QuestList> QuestsAsync(NameValueCollection query, CancellationToken ct) =>
        QueryAsync<QuestList>(
            async db =>
            {
                string campaign = (query["campaign"] ?? string.Empty).Trim();

                IQueryable<QuestEntity> questsQuery = db.Quests.AsNoTracking();
                if (campaign.Length > 0)
                {
                    questsQuery = questsQuery.Where(q => q.CampaignCode == campaign);
                }

                var rows = await questsQuery
                    .OrderBy(q => q.SortOrder)
                    .ThenBy(q => q.Id)
                    .Select(q => new
                    {
                        q.Id,
                        q.CampaignCode,
                        q.ChainCode,
                        q.LocalizationCode,
                        q.QuestType,
                        q.TargetType,
                        q.TargetValue,
                        q.Enabled,
                        q.TotalSteps,
                        q.RewardType,
                        q.RewardAmount,
                        q.ImageVersion,
                        q.SortOrder,
                        q.Easy,
                        q.Seasonal,
                        q.SeasonalSeconds,
                        q.EndsAt,
                        acceptedCount = db.PlayerQuests.Count(p =>
                            p.QuestEntityId == q.Id && p.Accepted
                        ),
                        completedCount = db.PlayerQuests.Count(p =>
                            p.QuestEntityId == q.Id && p.Completed
                        ),
                    })
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                DateTime now = DateTime.UtcNow;
                List<QuestRow> items = rows.Select(q => new QuestRow(
                        q.Id,
                        // image_version *is* the asset filename, so a quest with an empty one shows
                        // no picture in the client either -- which is worth seeing here.
                        _assetUrls.QuestImage(q.ImageVersion),
                        q.CampaignCode,
                        q.ChainCode,
                        q.LocalizationCode,
                        q.QuestType,
                        q.TargetType,
                        q.TargetValue,
                        q.Enabled,
                        q.TotalSteps,
                        q.RewardType,
                        q.RewardAmount,
                        q.RewardType < 0 ? "credits" : "activityPoints",
                        q.SortOrder,
                        q.Easy,
                        q.Seasonal,
                        q.SeasonalSeconds,
                        q.EndsAt,
                        q.Seasonal && q.EndsAt is { } endsAt && endsAt <= now,
                        q.acceptedCount,
                        q.completedCount
                    ))
                    .ToList();

                List<string> campaigns = rows.Select(q => q.CampaignCode)
                    .Distinct()
                    .OrderBy(c => c, StringComparer.Ordinal)
                    .ToList();

                return new QuestList(items.Count, campaigns, items);
            },
            ct
        );

    /// <summary>One quest with its full field set plus its lifetime accept/complete totals.</summary>
    public Task<QuestDetail?> QuestDetailAsync(int questId, CancellationToken ct) =>
        QueryAsync<QuestDetail?>(
            async db =>
            {
                QuestEntity? quest = await db
                    .Quests.AsNoTracking()
                    .FirstOrDefaultAsync(q => q.Id == questId, ct)
                    .ConfigureAwait(false);

                if (quest is null)
                {
                    return null;
                }

                int acceptedCount = await db
                    .PlayerQuests.AsNoTracking()
                    .CountAsync(p => p.QuestEntityId == questId && p.Accepted, ct)
                    .ConfigureAwait(false);

                int completedCount = await db
                    .PlayerQuests.AsNoTracking()
                    .CountAsync(p => p.QuestEntityId == questId && p.Completed, ct)
                    .ConfigureAwait(false);

                DateTime now = DateTime.UtcNow;

                return new QuestDetail(
                    quest.Id,
                    quest.CampaignCode,
                    quest.ChainCode,
                    quest.LocalizationCode,
                    quest.QuestType,
                    quest.TargetType,
                    quest.TargetValue,
                    quest.Enabled,
                    quest.TotalSteps,
                    quest.RewardType,
                    quest.RewardAmount,
                    quest.RewardType < 0 ? "credits" : "activityPoints",
                    quest.CatalogPageName,
                    quest.ImageVersion,
                    _assetUrls.QuestImage(quest.ImageVersion),
                    quest.SortOrder,
                    quest.Easy,
                    quest.Seasonal,
                    quest.SeasonalSeconds,
                    quest.EndsAt,
                    quest.Seasonal && quest.EndsAt is { } endsAt && endsAt <= now,
                    acceptedCount,
                    completedCount
                );
            },
            ct
        );

    /// <summary>Quest completions over time and the most-completed quests, aggregated from
    /// <c>player_quests</c>.</summary>
    public Task<QuestStats> QuestsStatsAsync(NameValueCollection query, CancellationToken ct) =>
        QueryAsync<QuestStats>(
            async db =>
            {
                (DateTime since, DateTime until) = TimeWindow.Resolve(query, DateTime.UtcNow);
                string granularity = TimeWindow.Granularity(query["granularity"]);

                List<(int QuestId, DateTime CompletedAt)> completions = await db
                    .PlayerQuests.AsNoTracking()
                    .Where(p =>
                        p.Completed
                        && p.CompletedAt != null
                        && p.CompletedAt >= since
                        && p.CompletedAt <= until
                    )
                    .Select(p => new ValueTuple<int, DateTime>(
                        p.QuestEntityId,
                        p.CompletedAt!.Value
                    ))
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                int totalCompletions = completions.Count;
                int totalAccepted = await db
                    .PlayerQuests.AsNoTracking()
                    .CountAsync(p => p.Accepted, ct)
                    .ConfigureAwait(false);
                int activePlayers = await db
                    .PlayerQuests.AsNoTracking()
                    .Where(p => p.Accepted && !p.Completed)
                    .Select(p => p.PlayerEntityId)
                    .Distinct()
                    .CountAsync(ct)
                    .ConfigureAwait(false);

                Dictionary<DateTime, int> bucketMap = new();
                DateTime cursor = TimeWindow.Bucket(since, granularity);
                DateTime end = TimeWindow.Bucket(until, granularity);

                while (cursor <= end)
                {
                    bucketMap[cursor] = 0;
                    cursor = TimeWindow.NextBucket(cursor, granularity);
                }

                foreach ((int _, DateTime completedAt) in completions)
                {
                    DateTime bucket = TimeWindow.Bucket(completedAt, granularity);
                    bucketMap[bucket] = bucketMap.GetValueOrDefault(bucket) + 1;
                }

                List<QuestCompletionPoint> timeline = bucketMap
                    .OrderBy(pair => pair.Key)
                    .Select(pair => new QuestCompletionPoint(
                        pair.Key.ToString("O"),
                        TimeWindow.Label(pair.Key, granularity),
                        pair.Value
                    ))
                    .ToList();

                List<int> topQuestIds = completions
                    .GroupBy(c => c.QuestId)
                    .OrderByDescending(g => g.Count())
                    .Take(10)
                    .Select(g => g.Key)
                    .ToList();

                Dictionary<int, int> completionsByQuest = completions
                    .GroupBy(c => c.QuestId)
                    .ToDictionary(g => g.Key, g => g.Count());

                Dictionary<int, string> questNames = await db
                    .Quests.AsNoTracking()
                    .Where(q => topQuestIds.Contains(q.Id))
                    .ToDictionaryAsync(
                        q => q.Id,
                        q => q.CampaignCode + "." + q.LocalizationCode,
                        ct
                    )
                    .ConfigureAwait(false);

                List<QuestCompletionCount> topQuests = topQuestIds
                    .Select(id => new QuestCompletionCount(
                        id,
                        questNames.GetValueOrDefault(id, $"quest #{id}"),
                        completionsByQuest.GetValueOrDefault(id)
                    ))
                    .ToList();

                return new QuestStats(
                    new ReportWindow(since, until, granularity),
                    new QuestTotals(totalCompletions, totalAccepted, activePlayers),
                    timeline,
                    topQuests
                );
            },
            ct
        );
}
