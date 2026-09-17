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
using Vortex.Database.Entities.Quests;
using Vortex.Primitives.Crypto;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Quests.Grains;
using Vortex.Progression.Quests;
using Vortex.Protocol.Messages.Outgoing.Quest;

namespace Vortex.Progression.Grains;

/// <summary>
/// Owns the active community goal. Kept alive because it holds the hotel's running total: rebuilding
/// that from a sum over every contribution on each activation would make the landing view's
/// five-second poll a table scan.
/// </summary>
/// <remarks>
/// It also decides when a rung's coin reward falls due, and that is ALL it does about it: it writes
/// rows into <c>community_goal_payouts</c> and never touches the chain. Minting is an Ethereum
/// transaction — seconds, and fallible — and this grain is single-threaded and holds the hotel's
/// running total, so awaiting one here would queue every other player's contribution behind the
/// chain. <c>CommunityGoalPayoutService</c> drains the rows.
/// </remarks>
[KeepAlive]
internal sealed class CommunityGoalGrain(
    IGrainFactory grainFactory,
    IDbContextFactory<VortexDbContext> dbCtxFactory,
    IHotelWalletService hotelWallets,
    ILogger<CommunityGoalGrain> logger
) : Grain, ICommunityGoalGrain
{
    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly IDbContextFactory<VortexDbContext> _dbCtxFactory = dbCtxFactory;
    private readonly IHotelWalletService _hotelWallets = hotelWallets;
    private readonly ILogger<CommunityGoalGrain> _logger = logger;

    private CommunityGoalEntity? _goal;
    private ImmutableArray<CommunityGoalRung> _rungs = ImmutableArray<CommunityGoalRung>.Empty;
    private int _totalScore;
    private int _rewardedLevel;
    private bool _loaded;

    public override async Task OnActivateAsync(CancellationToken ct)
    {
        await LoadAsync(ct).ConfigureAwait(true);

        // The catch-up half of the "two SaveChanges" note in ContributeAsync: a rung that was
        // cleared but not yet paid when the hotel went down is settled here, including the case
        // nothing else would ever fix — the FINAL rung, after which no contribution arrives to
        // trigger the check again.
        await AwardClearedRungsAsync(ct).ConfigureAwait(true);

        await base.OnActivateAsync(ct).ConfigureAwait(true);
    }

    public async Task SendProgressAsync(int playerId, CancellationToken ct)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(true);

        if (_goal is null)
        {
            return;
        }

        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(true);

        int personalScore = await dbCtx
            .PlayerCommunityGoalContributions.AsNoTracking()
            .Where(c => c.CommunityGoalEntityId == _goal.Id && c.PlayerEntityId == playerId)
            .Select(c => c.Score)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(true);

        // Rank is "how many contributors are strictly ahead, plus one", counted in the database so a
        // hotel with thousands of contributors never materialises the list to find one place.
        int rank =
            personalScore <= 0
                ? 0
                : await dbCtx
                    .PlayerCommunityGoalContributions.AsNoTracking()
                    .CountAsync(
                        c => c.CommunityGoalEntityId == _goal.Id && c.Score > personalScore,
                        ct
                    )
                    .ConfigureAwait(true) + 1;

        CommunityGoalStanding standing = CommunityGoalLadder.Resolve(_rungs, _totalScore);

        await _grainFactory
            .GetPlayerPresenceGrain(playerId)
            .SendComposerAsync(
                new CommunityGoalProgressMessageComposer
                {
                    HasGoalExpired = HasExpired(),
                    PersonalContributionScore = personalScore,
                    PersonalContributionRank = rank,
                    CommunityTotalScore = _totalScore,
                    CommunityHighestAchievedLevel = standing.HighestAchievedLevel,
                    ScoreRemainingUntilNextLevel = standing.ScoreRemainingUntilNextLevel,
                    PercentCompletionTowardsNextLevel = standing.PercentCompletionTowardsNextLevel,
                    GoalCode = _goal.Code,
                    TimeRemainingInSeconds = SecondsRemaining(),
                    RewardUserLimits = [.. CommunityGoalLadder.RewardUserLimits(_rungs)],
                }
            )
            .ConfigureAwait(true);
    }

    public async Task SendHallOfFameAsync(int playerId, int limit, CancellationToken ct)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(true);

        if (_goal is null)
        {
            return;
        }

        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(true);

        var top = await dbCtx
            .PlayerCommunityGoalContributions.AsNoTracking()
            .Where(c => c.CommunityGoalEntityId == _goal.Id && c.Score > 0)
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.PlayerEntityId)
            .Take(Math.Max(1, limit))
            .Select(c => new
            {
                c.PlayerEntityId,
                c.Score,
                Name = c.PlayerEntity != null ? c.PlayerEntity.Name : null,
                Figure = c.PlayerEntity != null ? c.PlayerEntity.Figure : null,
            })
            .ToListAsync(ct)
            .ConfigureAwait(true);

        ImmutableArray<CommunityGoalHallOfFameEntry> entries =
        [
            .. top.Select(
                (row, index) =>
                    new CommunityGoalHallOfFameEntry
                    {
                        UserId = row.PlayerEntityId,
                        UserName = row.Name ?? string.Empty,
                        Figure = row.Figure ?? string.Empty,
                        Rank = index + 1,
                        CurrentScore = row.Score,
                    }
            ),
        ];

        await _grainFactory
            .GetPlayerPresenceGrain(playerId)
            .SendComposerAsync(
                new CommunityGoalHallOfFameMessageComposer
                {
                    GoalCode = _goal.Code,
                    Entries = entries,
                }
            )
            .ConfigureAwait(true);
    }

    public async Task ContributeAsync(
        int playerId,
        string campaignCode,
        int completions,
        CancellationToken ct
    )
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(true);

        if (_goal is null || completions <= 0 || HasExpired())
        {
            return;
        }

        // A goal is fed by one campaign. Without this check every quest completion in the hotel would
        // pour into whatever goal happens to be active.
        if (
            _goal.CampaignCode.Length == 0
            || !_goal.CampaignCode.Equals(campaignCode, StringComparison.OrdinalIgnoreCase)
        )
        {
            return;
        }

        // What a finished quest is worth, which the goal owns and the caller does not. It was being
        // ignored: every completion counted as exactly 1 whatever `score_per_quest` said, so the
        // one knob for pacing a campaign without rewriting its whole ladder did nothing. Clamped at
        // 1 so a row left at 0 does not make the bar unfillable.
        int amount = completions * Math.Max(1, _goal.ScorePerQuest);

        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(true);

        PlayerCommunityGoalContributionEntity? contribution = await dbCtx
            .PlayerCommunityGoalContributions.FirstOrDefaultAsync(
                c => c.CommunityGoalEntityId == _goal.Id && c.PlayerEntityId == playerId,
                ct
            )
            .ConfigureAwait(true);

        if (contribution is null)
        {
            dbCtx.PlayerCommunityGoalContributions.Add(
                new PlayerCommunityGoalContributionEntity
                {
                    PlayerEntityId = playerId,
                    CommunityGoalEntityId = _goal.Id,
                    Score = amount,
                    LastContributedAt = DateTime.UtcNow,
                }
            );
        }
        else
        {
            contribution.Score += amount;
            contribution.LastContributedAt = DateTime.UtcNow;
        }

        await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);

        // The grain is single-threaded, so the cached total stays exact without a re-sum.
        _totalScore += amount;

        // Saved FIRST, then the rung check, because the winners are read back out of the database
        // and this contribution has to be among them — the player whose quest tipped the bar over
        // the threshold is exactly the one a merge-in-memory version would get wrong.
        //
        // Two SaveChanges therefore, and a crash between them leaves the rung cleared but unpaid.
        // That heals: `rewarded_level` has not moved, so the next contribution — or the next
        // activation, which runs the same check — pays it. It cannot double-pay, because the same
        // `rewarded_level` is what the check reads.
        await AwardClearedRungsAsync(ct).ConfigureAwait(true);

        await SendProgressAsync(playerId, ct).ConfigureAwait(true);
    }

    public Task ReloadAsync(CancellationToken ct) => LoadAsync(ct);

    /// <summary>
    /// Writes the coin rewards owed for every rung the hotel total has cleared and that has not been
    /// settled yet, and advances <c>rewarded_level</c> past all of them in the same save.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The winners are read out of the database in the SAME order the hall of fame uses — score
    /// descending, then player id — so what the players saw when the bar filled is what gets paid.
    /// They are then written down rather than recomputed at minting time, because contributions
    /// keep arriving and the list would drift.
    /// </para>
    /// <para>
    /// <c>rewarded_level</c> advances past cleared rungs that pay nothing too, so it means "the
    /// highest rung already settled" and not "the highest rung that happened to cost coins". A
    /// consequence worth knowing: adding a coin reward to a rung the hotel already cleared does
    /// nothing. That is deliberate — a rung pays the people who were on top when it fell, and
    /// nobody is on top of a rung that fell last week.
    /// </para>
    /// </remarks>
    private async Task AwardClearedRungsAsync(CancellationToken ct)
    {
        if (_goal is null || _rungs.IsEmpty)
        {
            return;
        }

        // All of the arithmetic, and none of it here: see CommunityGoalLadder.Settle for the three
        // things it gets right that are easy to get wrong.
        CommunityGoalSettlement settlement = CommunityGoalLadder.Settle(
            _rungs,
            _totalScore,
            _rewardedLevel
        );

        if (settlement.RewardedLevel <= _rewardedLevel)
        {
            return;
        }

        int highestCleared = settlement.RewardedLevel;
        IReadOnlyList<CommunityGoalRung> due = settlement.Due;

        // No keys, no wallets: GetAddress still answers, but nothing on any chain will ever be found
        // at what it answers. The ladder still moves on — holding it back would re-log this on every
        // contribution for the rest of the campaign — and the warning says what was skipped.
        if (due.Count > 0 && !_hotelWallets.Enabled)
        {
            _logger.LogWarning(
                "Community goal {GoalCode} cleared rung(s) up to {Level} paying coins, but no hotel "
                    + "wallet is configured — nothing was awarded.",
                _goal.Code,
                highestCleared
            );

            due = [];
        }

        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(true);

        foreach (CommunityGoalRung rung in due)
        {
            var winners = await dbCtx
                .PlayerCommunityGoalContributions.AsNoTracking()
                .Where(c => c.CommunityGoalEntityId == _goal.Id && c.Score > 0)
                .OrderByDescending(c => c.Score)
                .ThenBy(c => c.PlayerEntityId)
                .Take(rung.RewardUserLimit)
                .Select(c => new { c.PlayerEntityId, c.Score })
                .ToListAsync(ct)
                .ConfigureAwait(true);

            for (int i = 0; i < winners.Count; i++)
            {
                dbCtx.CommunityGoalPayouts.Add(
                    new CommunityGoalPayoutEntity
                    {
                        CommunityGoalEntityId = _goal.Id,
                        LevelNumber = rung.LevelNumber,
                        PlayerEntityId = winners[i].PlayerEntityId,
                        Rank = i + 1,
                        Score = winners[i].Score,
                        Address = _hotelWallets.GetAddress(winners[i].PlayerEntityId),
                        AmountCoins = rung.RewardCoins,
                    }
                );
            }

            _logger.LogInformation(
                "Community goal {GoalCode} rung {Level} reached: {Count} contributor(s) owed "
                    + "{Amount} VTX each.",
                _goal.Code,
                rung.LevelNumber,
                winners.Count,
                rung.RewardCoins
            );
        }

        CommunityGoalEntity? tracked = await dbCtx
            .CommunityGoals.FirstOrDefaultAsync(g => g.Id == _goal.Id, ct)
            .ConfigureAwait(true);

        if (tracked is null)
        {
            // The goal was deleted between the contribution and here. The payout rows would point at
            // nothing, so none of this is saved.
            return;
        }

        tracked.RewardedLevel = highestCleared;

        try
        {
            await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);
        }
        catch (DbUpdateException ex)
        {
            // The unique index on (goal, level, player) is what stops a rung paying twice. Reaching
            // it means something already wrote these rows, so the reward is not lost — but this
            // grain is a cluster-wide singleton and should be the only writer, which makes it worth
            // an error rather than a shrug.
            _logger.LogError(
                ex,
                "Community goal {GoalCode} payouts for rung(s) up to {Level} were rejected; they "
                    + "were most likely already written.",
                _goal.Code,
                highestCleared
            );

            return;
        }

        _rewardedLevel = highestCleared;
    }

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (!_loaded)
        {
            await LoadAsync(ct).ConfigureAwait(true);
        }
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        try
        {
            await using VortexDbContext dbCtx = await _dbCtxFactory
                .CreateDbContextAsync(ct)
                .ConfigureAwait(true);

            DateTime now = DateTime.UtcNow;

            // "Active" = enabled and not past its deadline, lowest sort order first. One goal runs at
            // a time; the client has no way to show two.
            _goal = await dbCtx
                .CommunityGoals.AsNoTracking()
                .Where(g => g.Enabled && (g.EndsAt == null || g.EndsAt > now))
                .OrderBy(g => g.SortOrder)
                .ThenBy(g => g.Id)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(true);

            if (_goal is null)
            {
                _rungs = ImmutableArray<CommunityGoalRung>.Empty;
                _totalScore = 0;
                _rewardedLevel = 0;
                _loaded = true;

                return;
            }

            _rewardedLevel = _goal.RewardedLevel;

            _rungs =
            [
                .. (
                    await dbCtx
                        .CommunityGoalLevels.AsNoTracking()
                        .Where(l => l.CommunityGoalEntityId == _goal.Id)
                        .OrderBy(l => l.ScoreThreshold)
                        .ThenBy(l => l.LevelNumber)
                        .ToListAsync(ct)
                        .ConfigureAwait(true)
                ).Select(l => new CommunityGoalRung(
                    l.LevelNumber,
                    l.ScoreThreshold,
                    l.RewardUserLimit,
                    l.RewardCoins
                )),
            ];

            _totalScore = await dbCtx
                .PlayerCommunityGoalContributions.AsNoTracking()
                .Where(c => c.CommunityGoalEntityId == _goal.Id)
                .SumAsync(c => c.Score, ct)
                .ConfigureAwait(true);

            _loaded = true;

            _logger.LogInformation(
                "Community goal {GoalCode} active with {LevelCount} level(s), total {TotalScore}.",
                _goal.Code,
                _rungs.Length,
                _totalScore
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load the active community goal.");
        }
    }

    private bool HasExpired() => _goal?.EndsAt is { } endsAt && endsAt <= DateTime.UtcNow;

    private int SecondsRemaining()
    {
        if (_goal?.EndsAt is not { } endsAt)
        {
            return 0;
        }

        double seconds = (endsAt - DateTime.UtcNow).TotalSeconds;

        return seconds <= 0 ? 0 : (int)Math.Min(int.MaxValue, seconds);
    }
}
