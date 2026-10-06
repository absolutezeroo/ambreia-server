using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Vortex.Dashboard.API.Api.Progression.Contracts;
using Vortex.Dashboard.API.Infrastructure;
using Vortex.Database.Context;
using Vortex.Database.Entities.Achievements;

namespace Vortex.Dashboard.API.Api.Progression;

/// <summary>
/// Read + analytics surface for achievements. There is no achievement audit trail, so everything is
/// aggregated from <c>achievements</c>, <c>achievement_levels</c> and <c>player_achievements</c>.
/// <para>
/// The one thing a bare table dump cannot tell an operator is whether a definition can advance at
/// all: an achievement whose name no trigger ever calls is dead weight that still shows in the
/// client. <see cref="TriggeredAchievements"/> marks the ones a live trigger feeds, so a definition
/// with 0 holders reads as either "nobody got there yet" or "nothing can ever award this".
/// </para>
/// </summary>
internal sealed class AchievementReads(
    IDbContextFactory<VortexDbContext> dbContextFactory,
    DashboardAssetUrls assetUrls
) : DashboardReads(dbContextFactory)
{
    private readonly DashboardAssetUrls _assetUrls = assetUrls;

    /// <summary>Achievement names a live progression trigger calls today — mirrors
    /// <c>Vortex.Progression.Achievements.AchievementNames</c> and the call sites in
    /// <c>AchievementProgressEventHandlers</c>. Duplicated as strings because the dashboard does not
    /// reference <c>Vortex.Players</c>; a name that drifts shows up here as "not triggered".</summary>
    private static readonly HashSet<string> TriggeredAchievements = new(StringComparer.Ordinal)
    {
        "Login",
        "RoomEntry",
        "Motto",
        "AvatarLooks",
        "FriendListSize",
        "RoomDecoFurniCount",
        "RespectGiven",
        "RespectEarned",
    };

    /// <summary>Every achievement definition with its level ladder, total payout and how far the
    /// hotel has actually got through it. Optional <c>category</c> filter.</summary>
    public Task<AchievementListResponse> AchievementsAsync(
        NameValueCollection query,
        CancellationToken ct
    ) =>
        QueryAsync<AchievementListResponse>(
            async db =>
            {
                string category = (query["category"] ?? string.Empty).Trim();

                IQueryable<AchievementEntity> definitions = db.Achievements.AsNoTracking();
                if (category.Length > 0)
                {
                    definitions = definitions.Where(a => a.Category == category);
                }

                var headers = await definitions
                    .OrderBy(a => a.Category)
                    .ThenBy(a => a.Name)
                    .Select(a => new
                    {
                        a.Id,
                        a.Name,
                        a.Category,
                        a.DisplayMethod,
                    })
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                List<int> achievementIds = headers.Select(a => a.Id).ToList();

                List<AchievementLevelRow> levels = await db
                    .AchievementLevels.AsNoTracking()
                    .Where(l => achievementIds.Contains(l.AchievementEntityId))
                    .OrderBy(l => l.AchievementEntityId)
                    .ThenBy(l => l.Level)
                    .Select(l => new AchievementLevelRow(
                        l.AchievementEntityId,
                        l.Level,
                        l.BadgeCode,
                        l.ProgressRequirement,
                        l.RewardAmount,
                        l.RewardType,
                        l.ScorePoints
                    ))
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                Dictionary<int, List<AchievementLevelRow>> levelsByAchievement = levels
                    .GroupBy(l => l.AchievementId)
                    .ToDictionary(g => g.Key, g => g.ToList());

                // One grouped pass over player_achievements instead of subqueries per row: the table
                // grows with players × definitions, so per-row Count() would fan out badly. Grouping
                // by (achievement, level) keeps the completed-players count exact — a player has
                // finished the ladder when their level reaches the ladder's length.
                var progressRows = await db
                    .PlayerAchievements.AsNoTracking()
                    .Where(p => achievementIds.Contains(p.AchievementEntityId))
                    .GroupBy(p => new { p.AchievementEntityId, p.Level })
                    .Select(g => new
                    {
                        achievementId = g.Key.AchievementEntityId,
                        level = g.Key.Level,
                        players = g.Count(),
                        started = g.Count(p => p.Progress > 0),
                    })
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                Dictionary<int, AchievementProgressSummary> progressByAchievement = progressRows
                    .GroupBy(r => r.achievementId)
                    .ToDictionary(
                        g => g.Key,
                        g => new AchievementProgressSummary(
                            g.Sum(r => r.players),
                            g.Sum(r => r.started),
                            g.Sum(r => r.players * r.level),
                            g.Max(r => r.level),
                            g.ToDictionary(r => r.level, r => r.players)
                        )
                    );

                List<AchievementListItem> items = headers
                    .Select(a =>
                    {
                        List<AchievementLevelRow> ladder = levelsByAchievement.GetValueOrDefault(
                            a.Id,
                            []
                        );
                        AchievementProgressSummary? progress =
                            progressByAchievement.GetValueOrDefault(a.Id);
                        int levelCount = ladder.Count;

                        return new AchievementListItem(
                            a.Id,
                            a.Name,
                            a.Category,
                            a.DisplayMethod,
                            TriggeredAchievements.Contains(a.Name),
                            levelCount,
                            ladder.Sum(l => l.ScorePoints),
                            ladder.Where(l => l.RewardType < 0).Sum(l => l.RewardAmount),
                            ladder.Where(l => l.RewardType >= 0).Sum(l => l.RewardAmount),
                            levelCount > 0 ? ladder[levelCount - 1].ProgressRequirement : 0,
                            progress?.Players ?? 0,
                            progress?.Started ?? 0,
                            progress is null || levelCount == 0
                                ? 0
                                : progress
                                    .PlayersAtLevel.Where(p => p.Key >= levelCount)
                                    .Sum(p => p.Value),
                            progress?.LevelsAwarded ?? 0,
                            progress?.MaxLevelReached ?? 0,
                            // The badge of the last rung stands for the whole ladder in the list,
                            // the same picture the client puts on the achievement.
                            levelCount > 0
                                ? _assetUrls.BadgeImage(ladder[levelCount - 1].BadgeCode)
                                : null,
                            ladder
                                .Select(l => new AchievementLevel(
                                    l.Level,
                                    l.BadgeCode,
                                    _assetUrls.BadgeImage(l.BadgeCode),
                                    l.ProgressRequirement,
                                    l.RewardAmount,
                                    l.RewardType,
                                    l.RewardType < 0 ? "credits" : "activityPoints",
                                    l.ScorePoints
                                ))
                                .ToList()
                        );
                    })
                    .ToList();

                List<string> categories = await db
                    .Achievements.AsNoTracking()
                    .Select(a => a.Category)
                    .Distinct()
                    .OrderBy(c => c)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                return new AchievementListResponse(items.Count, categories, items);
            },
            ct
        );

    /// <summary>One achievement: its ladder, how many players sit at each level, and the players
    /// furthest along.</summary>
    public Task<AchievementDetail?> AchievementDetailAsync(
        int achievementId,
        CancellationToken ct
    ) =>
        QueryAsync<AchievementDetail?>(
            async db =>
            {
                AchievementEntity? achievement = await db
                    .Achievements.AsNoTracking()
                    .FirstOrDefaultAsync(a => a.Id == achievementId, ct)
                    .ConfigureAwait(false);

                if (achievement is null)
                {
                    return null;
                }

                // BadgeImage is not translatable to SQL, so the rungs are read first and the
                // urls attached in a second pass, like every other icon on this dashboard.
                var ladderRows = await db
                    .AchievementLevels.AsNoTracking()
                    .Where(l => l.AchievementEntityId == achievementId)
                    .OrderBy(l => l.Level)
                    .Select(l => new
                    {
                        l.Id,
                        l.Level,
                        l.BadgeCode,
                        l.ProgressRequirement,
                        l.RewardAmount,
                        l.RewardType,
                        l.ScorePoints,
                    })
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                List<AchievementLadderRung> ladder = ladderRows
                    .Select(l => new AchievementLadderRung(
                        l.Id,
                        l.Level,
                        l.BadgeCode,
                        _assetUrls.BadgeImage(l.BadgeCode),
                        l.ProgressRequirement,
                        l.RewardAmount,
                        l.RewardType,
                        l.RewardType < 0 ? "credits" : "activityPoints",
                        l.ScorePoints
                    ))
                    .ToList();

                List<AchievementLevelCount> levelDistribution = await db
                    .PlayerAchievements.AsNoTracking()
                    .Where(p => p.AchievementEntityId == achievementId)
                    .GroupBy(p => p.Level)
                    // Ordered before the projection: a positional record is opaque to the query
                    // translator, so ordering on one of its properties cannot be pushed down (see
                    // GroupReads).
                    .OrderBy(g => g.Key)
                    .Select(g => new AchievementLevelCount(g.Key, g.Count()))
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                var topPlayers = await db
                    .PlayerAchievements.AsNoTracking()
                    .Where(p => p.AchievementEntityId == achievementId)
                    .OrderByDescending(p => p.Level)
                    .ThenByDescending(p => p.Progress)
                    .Take(20)
                    .Select(p => new
                    {
                        playerId = p.PlayerEntityId,
                        p.Level,
                        p.Progress,
                        p.UpdatedAt,
                    })
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                Dictionary<int, string> names = await db.PlayerNamesAsync(
                        DisplayNameQueries.NormalizeIds(topPlayers.Select(p => (int?)p.playerId)),
                        ct
                    )
                    .ConfigureAwait(false);

                int levelCount = ladder.Count;
                int completed =
                    levelCount == 0
                        ? 0
                        : levelDistribution.Where(d => d.Level >= levelCount).Sum(d => d.Players);

                return new AchievementDetail(
                    achievement.Id,
                    achievement.Name,
                    achievement.Category,
                    achievement.DisplayMethod,
                    TriggeredAchievements.Contains(achievement.Name),
                    levelCount,
                    completed,
                    ladder,
                    levelDistribution,
                    topPlayers
                        .Select(p => new AchievementTopPlayer(
                            p.playerId,
                            DisplayNameQueries.ResolvePlayerName(names, p.playerId),
                            p.Level,
                            p.Progress,
                            p.UpdatedAt
                        ))
                        .ToList()
                );
            },
            ct
        );

    /// <summary>Hotel-wide achievement health: how much of the catalogue is reachable, how many
    /// badges the ladder has actually paid out, the score leaderboard, and — the useful one — the
    /// definitions nobody has ever progressed.</summary>
    public Task<AchievementStats> AchievementsStatsAsync(CancellationToken ct) =>
        QueryAsync<AchievementStats>(
            async db =>
            {
                var definitions = await db
                    .Achievements.AsNoTracking()
                    .Select(a => new
                    {
                        a.Id,
                        a.Name,
                        a.Category,
                    })
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                var levelStats = await db
                    .AchievementLevels.AsNoTracking()
                    .GroupBy(l => l.AchievementEntityId)
                    .Select(g => new
                    {
                        achievementId = g.Key,
                        levels = g.Count(),
                        score = g.Sum(l => l.ScorePoints),
                    })
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                var levelsByAchievement = levelStats.ToDictionary(l => l.achievementId);

                var progressStats = await db
                    .PlayerAchievements.AsNoTracking()
                    .GroupBy(p => p.AchievementEntityId)
                    .Select(g => new
                    {
                        achievementId = g.Key,
                        players = g.Count(),
                        levelsAwarded = g.Sum(p => p.Level),
                    })
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                var progressByAchievement = progressStats.ToDictionary(p => p.achievementId);

                int totalAchievements = definitions.Count;
                int totalLevels = levelStats.Sum(l => l.levels);
                int badgesAwarded = progressStats.Sum(p => p.levelsAwarded);
                int triggeredCount = definitions.Count(d => TriggeredAchievements.Contains(d.Name));

                int playersWithProgress = await db
                    .PlayerAchievements.AsNoTracking()
                    .Where(p => p.Progress > 0)
                    .Select(p => p.PlayerEntityId)
                    .Distinct()
                    .CountAsync(ct)
                    .ConfigureAwait(false);

                List<AchievementCategoryStats> byCategory = definitions
                    .GroupBy(d => d.Category)
                    .Select(g => new AchievementCategoryStats(
                        g.Key,
                        g.Count(),
                        g.Sum(d => levelsByAchievement.GetValueOrDefault(d.Id)?.levels ?? 0),
                        g.Sum(d =>
                            progressByAchievement.GetValueOrDefault(d.Id)?.levelsAwarded ?? 0
                        )
                    ))
                    .OrderByDescending(g => g.Achievements)
                    .ToList();

                // Nobody has ever moved on these: either the trigger is missing, or the requirement
                // is out of reach. The `triggered` flag separates the two cases.
                List<UntouchedAchievement> untouched = definitions
                    .Where(d => (progressByAchievement.GetValueOrDefault(d.Id)?.players ?? 0) == 0)
                    .Select(d => new UntouchedAchievement(
                        d.Id,
                        d.Name,
                        d.Category,
                        TriggeredAchievements.Contains(d.Name),
                        levelsByAchievement.GetValueOrDefault(d.Id)?.levels ?? 0
                    ))
                    .OrderBy(d => d.Category, StringComparer.Ordinal)
                    .ThenBy(d => d.Name, StringComparer.Ordinal)
                    .ToList();

                Dictionary<int, int> scoreByAchievement = levelStats.ToDictionary(
                    l => l.achievementId,
                    l => l.score
                );

                // The score leaderboard needs per-level scores, so it is built from the raw rows
                // rather than a SQL sum: a player's score is the sum of the *completed* levels'
                // points, not the achievement's full ladder.
                List<PlayerLevelRow> playerRows = await db
                    .PlayerAchievements.AsNoTracking()
                    .Where(p => p.Level > 0)
                    .Select(p => new PlayerLevelRow(
                        p.PlayerEntityId,
                        p.AchievementEntityId,
                        p.Level
                    ))
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                List<AchievementLevelRow> allLevels = await db
                    .AchievementLevels.AsNoTracking()
                    .Select(l => new AchievementLevelRow(
                        l.AchievementEntityId,
                        l.Level,
                        l.BadgeCode,
                        l.ProgressRequirement,
                        l.RewardAmount,
                        l.RewardType,
                        l.ScorePoints
                    ))
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                Dictionary<(int, int), int> scoreByLevel = allLevels
                    .GroupBy(l => (l.AchievementId, l.Level))
                    .ToDictionary(g => g.Key, g => g.First().ScorePoints);

                Dictionary<int, (int Score, int Badges)> perPlayer = new();
                foreach (PlayerLevelRow row in playerRows)
                {
                    int score = 0;
                    for (int level = 1; level <= row.Level; level++)
                    {
                        score += scoreByLevel.GetValueOrDefault((row.AchievementId, level));
                    }

                    (int Score, int Badges) current = perPlayer.GetValueOrDefault(row.PlayerId);
                    perPlayer[row.PlayerId] = (current.Score + score, current.Badges + row.Level);
                }

                List<KeyValuePair<int, (int Score, int Badges)>> topPlayerRows = perPlayer
                    .OrderByDescending(p => p.Value.Score)
                    .ThenByDescending(p => p.Value.Badges)
                    .Take(15)
                    .ToList();

                Dictionary<int, string> playerNames = await db.PlayerNamesAsync(
                        DisplayNameQueries.NormalizeIds(topPlayerRows.Select(p => (int?)p.Key)),
                        ct
                    )
                    .ConfigureAwait(false);

                List<AchievementScorePlayer> topPlayers = topPlayerRows
                    .Select(p => new AchievementScorePlayer(
                        p.Key,
                        DisplayNameQueries.ResolvePlayerName(playerNames, p.Key),
                        p.Value.Score,
                        p.Value.Badges
                    ))
                    .ToList();

                return new AchievementStats(
                    new AchievementStatsTotals(
                        totalAchievements,
                        totalLevels,
                        triggeredCount,
                        totalAchievements - triggeredCount,
                        badgesAwarded,
                        playersWithProgress,
                        scoreByAchievement.Values.Sum()
                    ),
                    byCategory,
                    untouched,
                    topPlayers,
                    // The level editor previews a badge code before its rung exists, so it needs the
                    // template rather than a resolved URL.
                    _assetUrls.BadgeImageTemplate
                );
            },
            ct
        );

    private sealed record AchievementLevelRow(
        int AchievementId,
        int Level,
        string BadgeCode,
        int ProgressRequirement,
        int RewardAmount,
        int RewardType,
        int ScorePoints
    );

    private sealed record PlayerLevelRow(int PlayerId, int AchievementId, int Level);

    private sealed record AchievementProgressSummary(
        int Players,
        int Started,
        int LevelsAwarded,
        int MaxLevelReached,
        Dictionary<int, int> PlayersAtLevel
    );
}
