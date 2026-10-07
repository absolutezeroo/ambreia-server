using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Vortex.Dashboard.API.Api.Hotel.Contracts;
using Vortex.Dashboard.API.Infrastructure;
using Vortex.Database.Context;
using Vortex.Database.Entities.Room;
using Vortex.Primitives.Bots;

namespace Vortex.Dashboard.API.Api.Hotel;

/// <summary>
/// Read surface for the room actors that are neither players nor furni: bots and the hand items
/// they (and players) pass around.
/// <para>
/// A bot's configuration lives in one <c>skills_json</c> blob keyed by the client's own skill ids,
/// so the raw row says nothing an operator can read. Every listing here decodes it: which buttons
/// the bot's menu will actually show, and — for the chatter skill, whose payload is the client's
/// <c>;#;</c> blob — how many phrases it has and whether it speaks on its own.
/// </para>
/// </summary>
internal sealed class BotReads(
    IDbContextFactory<VortexDbContext> dbContextFactory,
    DashboardAssetUrls assetUrls
) : DashboardReads(dbContextFactory)
{
    private readonly DashboardAssetUrls _assetUrls = assetUrls;

    /// <summary>Paginated bot roster. Filters: <c>q</c> (name/motto), <c>ownerId</c>, <c>roomId</c>,
    /// and <c>placed</c> (true = standing in a room, false = in its owner's hand).</summary>
    public Task<BotListResponse> BotsAsync(NameValueCollection query, CancellationToken ct) =>
        QueryAsync<BotListResponse>(
            async db =>
            {
                string term = (query["q"] ?? string.Empty).Trim();
                int limit = QueryValues.Limit(query["limit"], 40, 200);
                int page = QueryValues.Page(query["page"]);
                int offset = Math.Max(0, (page - 1) * limit);

                IQueryable<BotEntity> bots = db.Bots.AsNoTracking();

                if (term.Length > 0)
                {
                    bots = bots.Where(b => b.Name.Contains(term) || b.Motto.Contains(term));
                }

                if (int.TryParse(query["ownerId"], out int ownerId) && ownerId > 0)
                {
                    bots = bots.Where(b => b.OwnerPlayerEntityId == ownerId);
                }

                if (int.TryParse(query["roomId"], out int roomId) && roomId > 0)
                {
                    bots = bots.Where(b => b.RoomEntityId == roomId);
                }

                if (bool.TryParse(query["placed"], out bool placed))
                {
                    bots = placed
                        ? bots.Where(b => b.RoomEntityId != null)
                        : bots.Where(b => b.RoomEntityId == null);
                }

                int total = await bots.CountAsync(ct).ConfigureAwait(false);

                var rows = await bots.OrderByDescending(b => b.Id)
                    .Skip(offset)
                    .Take(limit)
                    .Select(b => new
                    {
                        b.Id,
                        b.Name,
                        b.Motto,
                        b.Figure,
                        gender = b.Gender.ToString(),
                        b.OwnerPlayerEntityId,
                        b.RoomEntityId,
                        b.X,
                        b.Y,
                        b.Z,
                        rotation = (int)b.Rotation,
                        b.SkillsJson,
                        b.CreatedAt,
                        b.UpdatedAt,
                    })
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                Dictionary<int, string> ownerNames = await db.PlayerNamesAsync(
                        DisplayNameQueries.NormalizeIds(
                            rows.Select(b => (int?)b.OwnerPlayerEntityId)
                        ),
                        ct
                    )
                    .ConfigureAwait(false);

                Dictionary<int, string> roomNames = await db.RoomNamesAsync(
                        DisplayNameQueries.NormalizeIds(rows.Select(b => b.RoomEntityId)),
                        ct
                    )
                    .ConfigureAwait(false);

                List<BotListItem> items = rows.Select(b =>
                    {
                        BotSkillSummary skills = SummarizeBotSkills(b.SkillsJson);

                        return new BotListItem(
                            b.Id,
                            b.Name,
                            b.Motto,
                            b.Figure,
                            _assetUrls.AvatarImage(b.Figure),
                            b.gender,
                            b.OwnerPlayerEntityId,
                            DisplayNameQueries.ResolvePlayerName(ownerNames, b.OwnerPlayerEntityId),
                            b.RoomEntityId,
                            b.RoomEntityId is { } id ? roomNames.GetValueOrDefault(id) : null,
                            b.RoomEntityId is not null,
                            b.X,
                            b.Y,
                            b.Z,
                            b.rotation,
                            skills.SkillIds,
                            skills.SkillNames,
                            skills.PhraseCount,
                            skills.AutoChat,
                            skills.DelaySeconds,
                            skills.Wanders,
                            skills.Dances,
                            b.CreatedAt,
                            b.UpdatedAt
                        );
                    })
                    .ToList();

                return new BotListResponse(page, limit, offset, total, items.Count, items);
            },
            ct
        );

    /// <summary>One bot with its decoded chatter phrases and its raw skill blob — the raw blob is
    /// kept because a configuration the decoder does not understand is exactly what an operator
    /// investigating a silent bot needs to see.</summary>
    public Task<BotDetail?> BotDetailAsync(int botId, CancellationToken ct) =>
        QueryAsync<BotDetail?>(
            async db =>
            {
                BotEntity? bot = await db
                    .Bots.AsNoTracking()
                    .FirstOrDefaultAsync(b => b.Id == botId, ct)
                    .ConfigureAwait(false);

                if (bot is null)
                {
                    return null;
                }

                Dictionary<int, string> ownerNames = await db.PlayerNamesAsync(
                        [bot.OwnerPlayerEntityId],
                        ct
                    )
                    .ConfigureAwait(false);

                Dictionary<int, string> roomNames = await db.RoomNamesAsync(
                        DisplayNameQueries.NormalizeIds([bot.RoomEntityId]),
                        ct
                    )
                    .ConfigureAwait(false);

                BotSkillSummary skills = SummarizeBotSkills(bot.SkillsJson);

                return new BotDetail(
                    bot.Id,
                    bot.Name,
                    bot.Motto,
                    bot.Figure,
                    _assetUrls.AvatarImage(bot.Figure),
                    bot.Gender.ToString(),
                    bot.OwnerPlayerEntityId,
                    DisplayNameQueries.ResolvePlayerName(ownerNames, bot.OwnerPlayerEntityId),
                    bot.RoomEntityId,
                    bot.RoomEntityId is { } id ? roomNames.GetValueOrDefault(id) : null,
                    bot.RoomEntityId is not null,
                    bot.X,
                    bot.Y,
                    bot.Z,
                    (int)bot.Rotation,
                    skills.SkillIds,
                    skills.SkillNames,
                    skills.Phrases,
                    skills.AutoChat,
                    skills.DelaySeconds,
                    skills.Markov,
                    skills.Wanders,
                    skills.Dances,
                    bot.SkillsJson,
                    bot.CreatedAt,
                    bot.UpdatedAt
                );
            },
            ct
        );

    /// <summary>Bot population health: how many exist, how many are actually standing in a room,
    /// how many were ever configured to say anything, and who owns them.</summary>
    public Task<BotStats> BotsStatsAsync(NameValueCollection query, CancellationToken ct) =>
        QueryAsync<BotStats>(
            async db =>
            {
                (DateTime since, DateTime until) = TimeWindow.Resolve(query, DateTime.UtcNow);
                string granularity = TimeWindow.Granularity(query["granularity"]);

                List<BotStatsRow> rows = await db
                    .Bots.AsNoTracking()
                    .Select(b => new BotStatsRow(
                        b.CreatedAt,
                        b.RoomEntityId,
                        b.OwnerPlayerEntityId,
                        b.Gender.ToString(),
                        b.SkillsJson
                    ))
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                List<BotSkillSummary> summaries = rows.ConvertAll(r =>
                    SummarizeBotSkills(r.SkillsJson)
                );

                int totalBots = rows.Count;
                int placedBots = rows.Count(r => r.RoomId is not null);
                int configuredBots = summaries.Count(s => s.SkillIds.Count > 0);
                int chattyBots = summaries.Count(s => s.PhraseCount > 0);
                int autoChatBots = summaries.Count(s => s.AutoChat);
                int wanderingBots = summaries.Count(s => s.Wanders);
                int dancingBots = summaries.Count(s => s.Dances);

                int distinctOwners = rows.Select(r => r.OwnerId).Distinct().Count();
                int roomsWithBots = rows.Where(r => r.RoomId is not null)
                    .Select(r => r.RoomId)
                    .Distinct()
                    .Count();

                List<BotGenderCount> byGender = rows.GroupBy(r => r.Gender)
                    .Select(g => new BotGenderCount(g.Key, g.Count()))
                    .OrderByDescending(g => g.Count)
                    .ToList();

                Dictionary<DateTime, int> bucketMap = new();
                DateTime cursor = TimeWindow.Bucket(since, granularity);
                DateTime end = TimeWindow.Bucket(until, granularity);

                while (cursor <= end)
                {
                    bucketMap[cursor] = 0;
                    cursor = TimeWindow.NextBucket(cursor, granularity);
                }

                foreach (
                    BotStatsRow row in rows.Where(r => r.CreatedAt >= since && r.CreatedAt <= until)
                )
                {
                    DateTime bucket = TimeWindow.Bucket(row.CreatedAt, granularity);
                    bucketMap[bucket] = bucketMap.GetValueOrDefault(bucket) + 1;
                }

                List<BotGrowthPoint> growth = bucketMap
                    .OrderBy(pair => pair.Key)
                    .Select(pair => new BotGrowthPoint(
                        pair.Key.ToString("O"),
                        TimeWindow.Label(pair.Key, granularity),
                        pair.Value
                    ))
                    .ToList();

                var topOwnerRows = rows.GroupBy(r => r.OwnerId)
                    .Select(g => new { ownerId = g.Key, botCount = g.Count() })
                    .OrderByDescending(g => g.botCount)
                    .Take(10)
                    .ToList();

                Dictionary<int, string> ownerNames = await db.PlayerNamesAsync(
                        DisplayNameQueries.NormalizeIds(topOwnerRows.Select(o => (int?)o.ownerId)),
                        ct
                    )
                    .ConfigureAwait(false);

                var topRoomRows = rows.Where(r => r.RoomId is not null)
                    .GroupBy(r => r.RoomId!.Value)
                    .Select(g => new { roomId = g.Key, botCount = g.Count() })
                    .OrderByDescending(g => g.botCount)
                    .Take(10)
                    .ToList();

                Dictionary<int, string> roomNames = await db.RoomNamesAsync(
                        DisplayNameQueries.NormalizeIds(topRoomRows.Select(r => (int?)r.roomId)),
                        ct
                    )
                    .ConfigureAwait(false);

                return new BotStats(
                    new ReportWindow(since, until, granularity),
                    new BotTotals(
                        totalBots,
                        placedBots,
                        totalBots - placedBots,
                        configuredBots,
                        chattyBots,
                        autoChatBots,
                        wanderingBots,
                        dancingBots,
                        distinctOwners,
                        roomsWithBots
                    ),
                    byGender,
                    growth,
                    topOwnerRows
                        .Select(o => new BotOwnerCount(
                            o.ownerId,
                            DisplayNameQueries.ResolvePlayerName(ownerNames, o.ownerId),
                            o.botCount
                        ))
                        .ToList(),
                    topRoomRows
                        .Select(r => new BotRoomCount(
                            r.roomId,
                            roomNames.GetValueOrDefault(r.roomId),
                            r.botCount
                        ))
                        .ToList()
                );
            },
            ct
        );

    /// <summary>Decodes <c>skills_json</c> (client skill id → payload) into something readable. A
    /// blob that will not parse yields an empty summary rather than throwing: one malformed bot must
    /// not take the whole listing down.</summary>
    private static BotSkillSummary SummarizeBotSkills(string? skillsJson)
    {
        if (string.IsNullOrWhiteSpace(skillsJson))
        {
            return BotSkillSummary.Empty;
        }

        Dictionary<string, string?>? payload;
        try
        {
            payload = JsonSerializer.Deserialize<Dictionary<string, string?>>(skillsJson);
        }
        catch (JsonException)
        {
            return BotSkillSummary.Empty with { Malformed = true };
        }

        if (payload is null || payload.Count == 0)
        {
            return BotSkillSummary.Empty;
        }

        List<int> skillIds = [];
        string? chatterData = null;

        foreach ((string key, string? value) in payload)
        {
            if (!int.TryParse(key, out int skillId))
            {
                continue;
            }

            skillIds.Add(skillId);

            if (skillId == BotSkillId.Chatter)
            {
                chatterData = value;
            }
        }

        skillIds.Sort();
        BotChatterConfiguration chatter = BotChatterConfiguration.Parse(chatterData);

        return new BotSkillSummary
        {
            SkillIds = skillIds,
            SkillNames = skillIds.ConvertAll(DescribeBotSkill),
            Phrases = [.. chatter.Phrases],
            PhraseCount = chatter.Phrases.Length,
            AutoChat = chatter.AutoChat,
            DelaySeconds = chatter.DelaySeconds,
            Markov = chatter.Markov,
            Wanders = skillIds.Contains(BotSkillId.RandomWalk),
            Dances = skillIds.Contains(BotSkillId.Dance),
        };
    }

    private static string DescribeBotSkill(int skillId) =>
        skillId switch
        {
            BotSkillId.DressUp => "dressUp",
            BotSkillId.Chatter => "chatter",
            BotSkillId.RandomWalk => "randomWalk",
            BotSkillId.Dance => "dance",
            BotSkillId.ChangeName => "changeName",
            BotSkillId.NoPickUp => "noPickUp",
            _ => $"skill{skillId}",
        };

    private sealed record BotStatsRow(
        DateTime CreatedAt,
        int? RoomId,
        int OwnerId,
        string Gender,
        string? SkillsJson
    );

    private sealed record BotSkillSummary
    {
        public static BotSkillSummary Empty { get; } = new();

        public List<int> SkillIds { get; init; } = [];
        public List<string> SkillNames { get; init; } = [];
        public List<string> Phrases { get; init; } = [];
        public int PhraseCount { get; init; }
        public bool AutoChat { get; init; }
        public int DelaySeconds { get; init; }
        public bool Markov { get; init; }
        public bool Wanders { get; init; }
        public bool Dances { get; init; }
        public bool Malformed { get; init; }
    }
}
