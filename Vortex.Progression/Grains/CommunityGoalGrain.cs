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
[KeepAlive]
internal sealed class CommunityGoalGrain(
    IGrainFactory grainFactory,
    IDbContextFactory<VortexDbContext> dbCtxFactory,
    ILogger<CommunityGoalGrain> logger
) : Grain, ICommunityGoalGrain
{
    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly IDbContextFactory<VortexDbContext> _dbCtxFactory = dbCtxFactory;
    private readonly ILogger<CommunityGoalGrain> _logger = logger;

    private CommunityGoalEntity? _goal;
    private ImmutableArray<CommunityGoalRung> _rungs = ImmutableArray<CommunityGoalRung>.Empty;
    private int _totalScore;
    private bool _loaded;

    public override async Task OnActivateAsync(CancellationToken ct)
    {
        await LoadAsync(ct).ConfigureAwait(true);

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

        await SendProgressAsync(playerId, ct).ConfigureAwait(true);
    }

    public Task ReloadAsync(CancellationToken ct) => LoadAsync(ct);

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
                _loaded = true;

                return;
            }

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
                    l.RewardUserLimit
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
