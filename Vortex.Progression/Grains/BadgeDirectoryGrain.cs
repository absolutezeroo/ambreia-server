using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orleans;
using Vortex.Database.Context;
using Vortex.Primitives.Badges;
using Vortex.Primitives.Badges.Grains;
using Vortex.Primitives.Badges.Snapshots;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Server;
using Vortex.Progression.Configuration;

namespace Vortex.Progression.Grains;

/// <summary>
/// Owner counts and rarity for every badge code in the hotel, counted once and served from memory.
/// </summary>
/// <remarks>
/// Two numbers ride on every badge the client draws — the owner count and the rarity tier — and
/// they are hotel-wide, not per player. Counting them inside each caller would mean a grouped query
/// on <c>player_badges</c> per inventory open, per avatar entering a room, per profile. So they are
/// counted here: once on activation, again on a timer, with grants and revokes nudging the numbers
/// in between so a freshly earned badge is not stale until the next sweep.
/// <para>
/// This is a read-through cache and writes nothing back, so there is nothing to flush on
/// deactivation. It is <c>[KeepAlive]</c> because reactivating means counting every badge row in
/// the hotel again while every badge read in flight waits behind it.
/// </para>
/// </remarks>
[KeepAlive]
internal sealed class BadgeDirectoryGrain(
    IDbContextFactory<VortexDbContext> dbCtxFactory,
    IGrainFactory grainFactory,
    ILogger<BadgeDirectoryGrain> logger
) : Grain, IBadgeDirectoryGrain
{
    /// <summary>The client asks in chunks of 50; anything larger is a crafted packet, not a board.</summary>
    private const int MaxChunkSize = 50;

    /// <summary>
    /// A ceiling on how many badge codes one rarity board may scan. A tier holding thousands of
    /// codes is a misconfigured threshold, and the query that follows is an <c>IN</c> list.
    /// </summary>
    private const int MaxRarityCodes = 500;

    /// <summary>Used when the counts have never loaded, so a failure reads as "no tier" and not as "unique".</summary>
    private static readonly BadgeInfoSnapshot Unknown = new()
    {
        BadgeCode = string.Empty,
        OwnerCount = 0,
        Rarity = BadgeRarityTier.Common,
    };

    private readonly IDbContextFactory<VortexDbContext> _dbCtxFactory = dbCtxFactory;
    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly ILogger<BadgeDirectoryGrain> _logger = logger;

    private Dictionary<string, int> _ownerCounts = new(StringComparer.OrdinalIgnoreCase);
    private BadgeRarityThresholds _thresholds = Defaults();
    private TimeSpan _refreshInterval = TimeSpan.FromSeconds(
        BadgeRarityConfig.RefreshSecondsDefault
    );
    private int _totalPlayers;
    private bool _loaded;

    public override async Task OnActivateAsync(CancellationToken ct)
    {
        await ReloadAsync(ct).ConfigureAwait(true);

        // The period is itself config, so it is read during the first load and the timer is armed
        // with whatever it found. A change to it takes effect on the next silo start; the counts
        // it governs are corrected by the grant/revoke path in the meantime either way.
        this.RegisterGrainTimer<object?>(
            static (self, tickCt) => ((BadgeDirectoryGrain)self!).ReloadAsync(tickCt),
            this,
            _refreshInterval,
            _refreshInterval
        );

        await base.OnActivateAsync(ct).ConfigureAwait(true);
    }

    public Task<ImmutableArray<BadgeInfoSnapshot>> GetInfoAsync(
        IReadOnlyList<string> badgeCodes,
        CancellationToken ct
    ) => Task.FromResult(badgeCodes.Select(Describe).ToImmutableArray());

    public Task<BadgeInfoSnapshot> GetInfoAsync(string badgeCode, CancellationToken ct) =>
        Task.FromResult(Describe(badgeCode));

    public Task<ImmutableArray<BadgeRarityCountSnapshot>> CountByRarityAsync(
        IReadOnlyList<string> badgeCodes,
        CancellationToken ct
    ) =>
        Task.FromResult(
            badgeCodes
                .Select(Describe)
                .GroupBy(info => info.Rarity)
                .OrderBy(group => group.Key)
                .Select(group => new BadgeRarityCountSnapshot
                {
                    Rarity = group.Key,
                    Count = group.Count(),
                })
                .ToImmutableArray()
        );

    /// <summary>
    /// Builds one chunk of a board by scoring every player, ordering, then cutting the window out.
    /// </summary>
    /// <remarks>
    /// The whole board is scored on each request. That is the deliberate ceiling here: the client
    /// already caches a chunk for 60 seconds and prefetches its neighbours, so the real request rate
    /// is low, and a cache on this side would have to be invalidated by every badge grant in the
    /// hotel — the thing the recount exists to avoid needing. If boards ever become hot, the fix is
    /// a per-(type, rarity) cached ranking refreshed on the same timer as the owner counts, not a
    /// smarter query.
    /// </remarks>
    public async Task<BadgeLeaderboardPageSnapshot> GetLeaderboardAsync(
        BadgeLeaderboardType type,
        int rarity,
        int chunkIndex,
        int chunkSize,
        int askingPlayerId,
        CancellationToken ct
    )
    {
        // The size is echoed, never replaced: the client throws the whole message away unless it
        // comes back as the 50 it asked for.
        BadgeLeaderboardPageSnapshot empty = new()
        {
            Type = type,
            Rarity = rarity,
            ChunkIndex = Math.Max(0, chunkIndex),
            ChunkSize = chunkSize,
            TotalEntries = 0,
            Entries = [],
            OwnEntry = null,
        };

        if (chunkSize <= 0 || chunkSize > MaxChunkSize || chunkIndex < 0)
        {
            return empty;
        }

        try
        {
            await using VortexDbContext dbCtx = await _dbCtxFactory
                .CreateDbContextAsync(ct)
                .ConfigureAwait(true);

            List<(int PlayerId, int Score)> scores = await ScoreAsync(dbCtx, type, rarity, ct)
                .ConfigureAwait(true);

            if (scores.Count == 0)
            {
                return empty;
            }

            // Ranked once, in memory: the rank of a row is its position in the whole board, so the
            // window cannot be cut before the ordering is settled.
            List<(int PlayerId, int Score, int Rank)> ranked =
            [
                .. scores
                    .OrderByDescending(s => s.Score)
                    .ThenBy(s => s.PlayerId)
                    .Select((s, index) => (s.PlayerId, s.Score, Rank: index + 1)),
            ];

            List<(int PlayerId, int Score, int Rank)> window =
            [
                .. ranked.Skip(chunkIndex * chunkSize).Take(chunkSize),
            ];

            (int PlayerId, int Score, int Rank) own = ranked.FirstOrDefault(r =>
                r.PlayerId == askingPlayerId
            );

            List<int> needed = [.. window.Select(w => w.PlayerId)];

            if (own.PlayerId != 0 && !needed.Contains(own.PlayerId))
            {
                needed.Add(own.PlayerId);
            }

            Dictionary<int, (string Name, string Figure)> players = await dbCtx
                .Players.AsNoTracking()
                .Where(p => needed.Contains(p.Id))
                .Select(p => new
                {
                    p.Id,
                    p.Name,
                    p.Figure,
                })
                .ToDictionaryAsync(p => p.Id, p => (p.Name, p.Figure), ct)
                .ConfigureAwait(true);

            return empty with
            {
                TotalEntries = ranked.Count,
                Entries = [.. window.Select(row => ToEntry(row, players))],
                OwnEntry = own.PlayerId == 0 ? null : ToEntry(own, players),
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to build badge leaderboard {Type} rarity {Rarity} chunk {Chunk}",
                type,
                rarity,
                chunkIndex
            );
            return empty;
        }
    }

    private static BadgeLeaderboardEntrySnapshot ToEntry(
        (int PlayerId, int Score, int Rank) row,
        Dictionary<int, (string Name, string Figure)> players
    )
    {
        players.TryGetValue(row.PlayerId, out (string Name, string Figure) player);

        return new BadgeLeaderboardEntrySnapshot
        {
            PlayerId = row.PlayerId,
            Name = player.Name ?? string.Empty,
            Figure = player.Figure ?? string.Empty,
            Rank = row.Rank,
            Score = row.Score,
        };
    }

    /// <summary>One score per player for the board asked for. An unknown board scores nobody.</summary>
    private async Task<List<(int PlayerId, int Score)>> ScoreAsync(
        VortexDbContext dbCtx,
        BadgeLeaderboardType type,
        int rarity,
        CancellationToken ct
    )
    {
        switch (type)
        {
            case BadgeLeaderboardType.TotalBadges:
                return await dbCtx
                    .PlayerBadges.AsNoTracking()
                    .GroupBy(b => b.PlayerEntityId)
                    .Select(g => new ValueTuple<int, int>(g.Key, g.Count()))
                    .ToListAsync(ct)
                    .ConfigureAwait(true);

            case BadgeLeaderboardType.ByRarity:
            {
                // The tier of a badge is this grain's own answer, so the codes of a tier are
                // resolved from the cached counts rather than asked of the database, which stores
                // no tier at all.
                List<string> codes =
                [
                    .. _ownerCounts
                        .Keys.Where(code => (int)Describe(code).Rarity == rarity)
                        .Take(MaxRarityCodes),
                ];

                if (codes.Count == 0)
                {
                    return [];
                }

                return await dbCtx
                    .PlayerBadges.AsNoTracking()
                    .Where(b => codes.Contains(b.BadgeCode))
                    .GroupBy(b => b.PlayerEntityId)
                    .Select(g => new ValueTuple<int, int>(g.Key, g.Count()))
                    .ToListAsync(ct)
                    .ConfigureAwait(true);
            }

            case BadgeLeaderboardType.AchievementLevel:
                // Levels cleared, not achievements touched -- the same sum the extended profile
                // puts on the wire, so a player's rank agrees with the number on their own profile.
                return await dbCtx
                    .PlayerAchievements.AsNoTracking()
                    .GroupBy(a => a.PlayerEntityId)
                    .Select(g => new ValueTuple<int, int>(g.Key, g.Sum(a => a.Level)))
                    .ToListAsync(ct)
                    .ConfigureAwait(true);

            default:
                return [];
        }
    }

    private BadgeInfoSnapshot Describe(string badgeCode)
    {
        if (string.IsNullOrWhiteSpace(badgeCode))
        {
            return Unknown;
        }

        int owners = Owners(badgeCode);

        return new BadgeInfoSnapshot
        {
            BadgeCode = badgeCode,
            OwnerCount = owners,
            Rarity = _loaded
                ? BadgeRarity.Calculate(owners, _totalPlayers, _thresholds)
                : BadgeRarityTier.Common,
        };
    }

    private int Owners(string badgeCode) =>
        _ownerCounts.TryGetValue(badgeCode, out int count) ? count : 0;

    /// <summary>
    /// Recounts from the database. A failure keeps whatever counts it already had and tries again
    /// on the next tick — serving slightly stale tiers beats blanking every badge in the hotel.
    /// </summary>
    private async Task ReloadAsync(CancellationToken ct)
    {
        try
        {
            ImmutableDictionary<string, string> config = await _grainFactory
                .GetServerConfigGrain()
                .GetManyAsync(BadgeRarityConfig.AllKeys)
                .ConfigureAwait(true);

            _thresholds = ReadThresholds(config);
            _refreshInterval = TimeSpan.FromSeconds(
                Math.Max(
                    30,
                    ServerConfigValues.GetInt(
                        config,
                        BadgeRarityConfig.RefreshSecondsKey,
                        BadgeRarityConfig.RefreshSecondsDefault
                    )
                )
            );

            await using VortexDbContext dbCtx = await _dbCtxFactory
                .CreateDbContextAsync(ct)
                .ConfigureAwait(true);

            List<KeyValuePair<string, int>> counts = await dbCtx
                .PlayerBadges.AsNoTracking()
                .GroupBy(badge => badge.BadgeCode)
                .Select(group => new KeyValuePair<string, int>(group.Key, group.Count()))
                .ToListAsync(ct)
                .ConfigureAwait(true);

            _totalPlayers = await dbCtx.Players.AsNoTracking().CountAsync(ct).ConfigureAwait(true);
            _ownerCounts = new Dictionary<string, int>(counts, StringComparer.OrdinalIgnoreCase);
            _loaded = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to recount badge owners; keeping the previous figures");
        }
    }

    private static BadgeRarityThresholds ReadThresholds(
        ImmutableDictionary<string, string> config
    ) =>
        new()
        {
            MinimumPlayers = ServerConfigValues.GetInt(
                config,
                BadgeRarityConfig.MinimumPlayersKey,
                BadgeRarityConfig.MinimumPlayersDefault
            ),
            UniqueMaxOwners = ServerConfigValues.GetInt(
                config,
                BadgeRarityConfig.UniqueMaxOwnersKey,
                BadgeRarityConfig.UniqueMaxOwnersDefault
            ),
            LegendaryMaxShare = ServerConfigValues.GetDouble(
                config,
                BadgeRarityConfig.LegendaryMaxShareKey,
                double.Parse(
                    BadgeRarityConfig.LegendaryMaxShareDefault,
                    System.Globalization.CultureInfo.InvariantCulture
                )
            ),
            MythicalMaxShare = ServerConfigValues.GetDouble(
                config,
                BadgeRarityConfig.MythicalMaxShareKey,
                double.Parse(
                    BadgeRarityConfig.MythicalMaxShareDefault,
                    System.Globalization.CultureInfo.InvariantCulture
                )
            ),
            EpicMaxShare = ServerConfigValues.GetDouble(
                config,
                BadgeRarityConfig.EpicMaxShareKey,
                double.Parse(
                    BadgeRarityConfig.EpicMaxShareDefault,
                    System.Globalization.CultureInfo.InvariantCulture
                )
            ),
            RareMaxShare = ServerConfigValues.GetDouble(
                config,
                BadgeRarityConfig.RareMaxShareKey,
                double.Parse(
                    BadgeRarityConfig.RareMaxShareDefault,
                    System.Globalization.CultureInfo.InvariantCulture
                )
            ),
            UncommonMaxShare = ServerConfigValues.GetDouble(
                config,
                BadgeRarityConfig.UncommonMaxShareKey,
                double.Parse(
                    BadgeRarityConfig.UncommonMaxShareDefault,
                    System.Globalization.CultureInfo.InvariantCulture
                )
            ),
        };

    private static BadgeRarityThresholds Defaults() =>
        ReadThresholds(ImmutableDictionary<string, string>.Empty);
}
