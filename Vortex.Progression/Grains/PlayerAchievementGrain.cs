using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Vortex.Database.Context;
using Vortex.Database.Entities.Achievements;
using Vortex.Primitives.Badges.Snapshots;
using Vortex.Primitives.Commerce;
using Vortex.Primitives.Events;
using Vortex.Primitives.Inventory.Grains;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Players.Snapshots;
using Vortex.Primitives.Players.Wallet;
using Vortex.Progression.Achievements;
using Vortex.Progression.Configuration;
using Vortex.Protocol.Messages.Outgoing.Inventory.Achievements;
using Vortex.Protocol.Messages.Outgoing.Notifications;

namespace Vortex.Progression.Grains;

/// <summary>
/// Per-player achievement grain. Holds the player's progress in memory for the life of the
/// activation, resolves it against the cached definitions into wire-ready snapshots (reads) and
/// applies progression end to end (writes): badge replacement, currency rewards, achievement-score
/// update and outbound composers. Orleans single-threading serialises progression per player, so no
/// locking is needed.
///
/// Writes are split by what is at stake. A level-up hands out a badge, currency and score, so its
/// row is written through immediately and nothing is granted until that write lands. Plain progress
/// — the common case, one counter bump per room entry, per respect, per furni placed — is only
/// marked dirty and batched by the flush timer, so a busy hotel does not pay two database
/// round-trips per player per event.
/// </summary>
internal sealed class PlayerAchievementGrain(
    IGrainFactory grainFactory,
    IDbContextFactory<VortexDbContext> dbCtxFactory,
    IOptions<AchievementConfig> achievementConfig,
    IEventPublisher events,
    ICommerceJournal journal,
    ILogger<PlayerAchievementGrain> logger
) : Grain, IPlayerAchievementGrain
{
    /// <summary>
    /// One achievement's live state. <see cref="EntityId"/> is 0 until the row exists in the
    /// database, and <see cref="Dirty"/> marks a progress counter the flush timer still owes a
    /// write. <see cref="LastProgressAt"/> backs the once-per-day guard.
    /// </summary>
    private sealed class ProgressState
    {
        public int EntityId { get; set; }
        public int Progress { get; set; }
        public int CompletedLevels { get; set; }
        public DateTime? LastProgressAt { get; set; }
        public bool Dirty { get; set; }
    }

    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly IDbContextFactory<VortexDbContext> _dbCtxFactory = dbCtxFactory;
    private readonly AchievementConfig _achievementConfig = achievementConfig.Value;
    private readonly IEventPublisher _events = events;
    private readonly ICommerceJournal _journal = journal;
    private readonly ILogger<PlayerAchievementGrain> _logger = logger;

    private readonly Dictionary<int, ProgressState> _stateByAchievementId = [];
    private bool _hydrated;
    private IDisposable? _timer;

    private int PlayerId => (int)this.GetPrimaryKeyLong();

    public override async Task OnActivateAsync(CancellationToken ct)
    {
        await EnsureHydratedAsync(ct).ConfigureAwait(true);

        TimeSpan interval = TimeSpan.FromMilliseconds(_achievementConfig.ProgressFlushIntervalMs);

        _timer = this.RegisterGrainTimer<object?>(
            static async (self, ct) =>
                await ((PlayerAchievementGrain)self!).FlushDirtyProgressAsync(ct),
            this,
            interval,
            interval
        );

        await base.OnActivateAsync(ct).ConfigureAwait(true);
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken ct)
    {
        _timer?.Dispose();

        await FlushDirtyProgressAsync(ct).ConfigureAwait(true);
    }

    public async Task<AchievementListSnapshot> GetAchievementsAsync(CancellationToken ct)
    {
        IAchievementManagerGrain manager = _grainFactory.GetAchievementManagerGrain();

        ImmutableArray<AchievementDefinitionSnapshot> definitions = await manager
            .GetDefinitionsAsync(ct)
            .ConfigureAwait(true);

        if (definitions.IsEmpty)
        {
            return new AchievementListSnapshot
            {
                Achievements = ImmutableArray<AchievementProgressSnapshot>.Empty,
                DefaultCategory = string.Empty,
                Score = 0,
            };
        }

        await EnsureHydratedAsync(ct).ConfigureAwait(true);

        ImmutableArray<AchievementProgressSnapshot>.Builder builder =
            ImmutableArray.CreateBuilder<AchievementProgressSnapshot>(definitions.Length);
        int score = 0;

        foreach (AchievementDefinitionSnapshot definition in definitions)
        {
            _stateByAchievementId.TryGetValue(definition.Id, out ProgressState? state);

            int cumulativeProgress = state?.Progress ?? 0;
            int completedLevels = state?.CompletedLevels ?? 0;

            builder.Add(
                AchievementProgressCalculator.Build(definition, cumulativeProgress, completedLevels)
            );
            score += AchievementProgressCalculator.ComputeScore(definition, completedLevels);
        }

        string defaultCategory = await manager.GetDefaultCategoryAsync(ct).ConfigureAwait(true);

        return new AchievementListSnapshot
        {
            Achievements = builder.ToImmutable(),
            DefaultCategory = defaultCategory,
            Score = score,
        };
    }

    public Task ProgressAsync(string achievementName, int amount, CancellationToken ct) =>
        ProgressCoreAsync(achievementName, amount, oncePerDay: false, absolute: false, ct);

    public Task ProgressDailyAsync(string achievementName, int amount, CancellationToken ct) =>
        ProgressCoreAsync(achievementName, amount, oncePerDay: true, absolute: false, ct);

    public async Task ProgressBabyPetColoursAsync(int petType, CancellationToken ct)
    {
        if (!BabyPetAchievements.ByPetType.TryGetValue(petType, out string? achievementName))
        {
            return;
        }

        await using VortexDbContext dbCtx = await dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(true);

        int colours = await dbCtx
            .Pets.AsNoTracking()
            .Where(p => p.OwnerPlayerEntityId == PlayerId && p.Type == petType)
            .Select(p => p.Color)
            .Distinct()
            .CountAsync(ct)
            .ConfigureAwait(true);

        await ProgressCoreAsync(achievementName, colours, oncePerDay: false, absolute: true, ct)
            .ConfigureAwait(true);
    }

    /// <param name="amount">
    /// Points to add, or — when <paramref name="absolute"/> — the total the bar should stand at.
    /// </param>
    private async Task ProgressCoreAsync(
        string achievementName,
        int amount,
        bool oncePerDay,
        bool absolute,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(achievementName) || amount <= 0)
        {
            return;
        }

        AchievementDefinitionSnapshot? definition = await _grainFactory
            .GetAchievementManagerGrain()
            .GetByNameAsync(achievementName, ct)
            .ConfigureAwait(true);

        if (definition is null)
        {
            _logger.LogWarning(
                "Progress requested for unknown achievement '{Name}' (player {PlayerId})",
                achievementName,
                PlayerId
            );
            return;
        }

        if (!await EnsureHydratedAsync(ct).ConfigureAwait(true))
        {
            _logger.LogWarning(
                "Dropping {Amount} progress on '{Name}' for player {PlayerId}: their stored progress could not be loaded, and advancing from an unknown baseline would overwrite it",
                amount,
                definition.Name,
                PlayerId
            );
            return;
        }

        if (!_stateByAchievementId.TryGetValue(definition.Id, out ProgressState? state))
        {
            state = new ProgressState();
            _stateByAchievementId[definition.Id] = state;
        }

        // An absolute call states where the bar should stand; the rest of this method only knows how
        // to add, so the difference is what it adds. Already there, or past it, is nothing to do.
        if (absolute)
        {
            amount -= state.Progress;

            if (amount <= 0)
            {
                return;
            }
        }

        DateTime now = DateTime.UtcNow;

        // Daily achievements (e.g. Login) advance at most once per calendar day. Tracked in memory
        // and hydrated from the row's last-write date, because the row itself now lags behind by up
        // to one flush interval.
        if (oncePerDay && state.LastProgressAt?.Date == now.Date)
        {
            return;
        }

        int previousCompleted = state.CompletedLevels;

        AchievementProgressResult result = AchievementProgressCalculator.ApplyProgress(
            definition,
            state.Progress,
            previousCompleted,
            amount
        );

        if (!result.ProgressChanged)
        {
            return;
        }

        if (result.LeveledUp)
        {
            // Rewards ride on this level, so the row has to be durable before any of them are
            // handed out. A failed write leaves the in-memory state untouched and gives up: the
            // next progress event tries the same level again.
            if (
                !await PersistAsync(
                        definition.Id,
                        state,
                        result.NewProgress,
                        result.NewCompletedLevels,
                        ct
                    )
                    .ConfigureAwait(true)
            )
            {
                return;
            }
        }
        else
        {
            state.Progress = result.NewProgress;
            state.Dirty = true;
        }

        state.LastProgressAt = now;

        IPlayerPresenceGrain presence = _grainFactory.GetPlayerPresenceGrain((long)PlayerId);

        if (result.LeveledUp)
        {
            await ApplyLevelUpsAsync(definition, previousCompleted, result, presence, ct)
                .ConfigureAwait(true);

            // A resolution statue can only be waiting on a level, so this is the one moment a
            // challenge can be won. One-way on purpose: progression must not wait on it.
            await _grainFactory
                .GetPlayerAchievementResolutionGrain((long)PlayerId)
                .OnAchievementLevelUpAsync(definition.Id, result.NewCompletedLevels, ct)
                .ConfigureAwait(true);
        }

        // Always refresh this achievement's row so an open achievements window updates live.
        await presence
            .SendComposerAsync(
                new AchievementEventMessageComposer
                {
                    Achievement = AchievementProgressCalculator.Build(
                        definition,
                        result.NewProgress,
                        result.NewCompletedLevels
                    ),
                }
            )
            .ConfigureAwait(true);
    }

    private async Task ApplyLevelUpsAsync(
        AchievementDefinitionSnapshot definition,
        int oldCompleted,
        AchievementProgressResult result,
        IPlayerPresenceGrain presence,
        CancellationToken ct
    )
    {
        IInventoryGrain inventory = _grainFactory.GetInventoryGrain((long)PlayerId);
        IPlayerWalletGrain wallet = _grainFactory.GetPlayerWalletGrain((long)PlayerId);

        string badgeHeldBefore =
            oldCompleted >= 1 ? definition.Levels[oldCompleted - 1].BadgeCode : string.Empty;
        string finalBadge = definition.Levels[result.NewCompletedLevels - 1].BadgeCode;

        int scoreGained = 0;
        int newScore = 0;

        // The level was persisted above before any of this ran, so from here the badge, the currency
        // and the score are owed. One operation for all three: they are one reward, and an operator
        // finding a half-paid level-up needs to see it as one thing (PROG-REWARD-032).
        await _journal
            .RecordOwedPayoutAsync(
                CommerceOperationKind.AchievementReward,
                (int)PlayerId,
                $"achievement={definition.Id} level={result.NewCompletedLevels} badge={finalBadge}",
                async innerCt =>
                {
                    // A player holds only the highest achievement badge: drop the previous one,
                    // grant the new.
                    if (!string.IsNullOrEmpty(badgeHeldBefore) && badgeHeldBefore != finalBadge)
                    {
                        await inventory
                            .RemoveBadgeAsync(badgeHeldBefore, innerCt)
                            .ConfigureAwait(true);
                    }

                    await inventory.GrantBadgeAsync(finalBadge, innerCt).ConfigureAwait(true);

                    foreach (AchievementLevelUp levelUp in result.LevelUps)
                    {
                        scoreGained += levelUp.ScorePoints;

                        if (levelUp.RewardAmount <= 0)
                        {
                            continue;
                        }

                        await wallet
                            .GrantCurrencyAsync(
                                CurrencyRewardRules.KindFor(levelUp.RewardType),
                                levelUp.RewardAmount,
                                innerCt
                            )
                            .ConfigureAwait(true);
                    }

                    newScore = await _grainFactory
                        .GetGrain<IPlayerGrain>((long)PlayerId)
                        .AddAchievementScoreAsync(scoreGained, innerCt)
                        .ConfigureAwait(true);
                },
                _logger,
                ct
            )
            .ConfigureAwait(true);

        AchievementLevelUp lastLevel = result.LevelUps[^1];

        // The badge the player just earned is counted a moment before this notification claims its
        // figures, so a first-ever holder reads as zero owners here until the directory recounts.
        // Asking anyway is still right: every other badge in the same dialog carries real numbers.
        BadgeInfoSnapshot badgeInfo = await _grainFactory
            .GetBadgeDirectoryGrain()
            .GetInfoAsync(finalBadge, ct)
            .ConfigureAwait(true);

        await presence
            .SendComposerAsync(
                new HabboAchievementNotificationMessageComposer
                {
                    Type = 0,
                    Level = result.NewCompletedLevels,
                    BadgeId = 0,
                    BadgeCode = finalBadge,
                    Points = scoreGained,
                    LevelRewardPoints = lastLevel.RewardAmount,
                    LevelRewardPointType = lastLevel.RewardType,
                    BonusPoints = 0,
                    AchievementId = definition.Id,
                    RemovedBadgeCode = badgeHeldBefore,
                    Category = definition.Category,
                    ShowDialogToUser = true,
                    OwnerCount = badgeInfo.OwnerCount,
                    BadgeRarityId = (int)badgeInfo.Rarity,
                }
            )
            .ConfigureAwait(true);

        await presence
            .SendComposerAsync(new AchievementsScoreEventMessageComposer { Score = newScore })
            .ConfigureAwait(true);

        // Published last, once the badge, the currency and the score have all landed: an audit line
        // that says a player unlocked something they do not hold is worse than no line at all.
        await _events
            .PublishAsync(
                new AchievementLevelUpEvent(
                    PlayerId,
                    definition.Id,
                    definition.Name,
                    result.NewCompletedLevels,
                    finalBadge,
                    scoreGained
                ),
                ct
            )
            .ConfigureAwait(true);
    }

    /// <summary>
    /// Writes one achievement's row through immediately and, only once that lands, commits the new
    /// figures into the in-memory state. Returns false on failure with the state untouched, so the
    /// caller never grants a reward for a level the database did not take.
    /// </summary>
    private async Task<bool> PersistAsync(
        int achievementId,
        ProgressState state,
        int progress,
        int completedLevels,
        CancellationToken ct
    )
    {
        try
        {
            await using VortexDbContext dbCtx = await _dbCtxFactory
                .CreateDbContextAsync(ct)
                .ConfigureAwait(true);

            PlayerAchievementEntity row = BuildRow(
                achievementId,
                state.EntityId,
                progress,
                completedLevels
            );

            if (state.EntityId == 0)
            {
                dbCtx.PlayerAchievements.Add(row);
            }
            else
            {
                TrackProgressUpdate(dbCtx, row, includeLevel: true);
            }

            await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);

            state.EntityId = row.Id;
            state.Progress = progress;
            state.CompletedLevels = completedLevels;
            state.Dirty = false;

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to persist achievement level-up for player {PlayerId}, achievement {AchievementId}",
                PlayerId,
                achievementId
            );
            return false;
        }
    }

    /// <summary>
    /// Writes the progress counters the timer owes, in one batch. Counters only: a level change is
    /// already durable by the time it reaches here (see <see cref="PersistAsync"/>). Rows stay dirty
    /// when the write fails, so the next tick retries them instead of dropping them.
    /// </summary>
    private async Task FlushDirtyProgressAsync(CancellationToken ct)
    {
        KeyValuePair<int, ProgressState>[] batch = _stateByAchievementId
            .Where(entry => entry.Value.Dirty)
            .Take(_achievementConfig.MaxDirtyProgressPerFlush)
            .ToArray();

        if (batch.Length == 0)
        {
            return;
        }

        try
        {
            await using VortexDbContext dbCtx = await _dbCtxFactory
                .CreateDbContextAsync(ct)
                .ConfigureAwait(true);

            List<(ProgressState State, PlayerAchievementEntity Row)> inserted = [];

            foreach ((int achievementId, ProgressState state) in batch)
            {
                PlayerAchievementEntity row = BuildRow(
                    achievementId,
                    state.EntityId,
                    state.Progress,
                    state.CompletedLevels
                );

                if (state.EntityId == 0)
                {
                    dbCtx.PlayerAchievements.Add(row);
                    inserted.Add((state, row));
                    continue;
                }

                TrackProgressUpdate(dbCtx, row, includeLevel: false);
            }

            await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);

            foreach ((ProgressState state, PlayerAchievementEntity row) in inserted)
            {
                state.EntityId = row.Id;
            }

            foreach ((_, ProgressState state) in batch)
            {
                state.Dirty = false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to flush {Count} dirty achievement rows for player {PlayerId}",
                batch.Length,
                PlayerId
            );
        }
    }

    /// <summary>
    /// Loads the player's stored rows into memory, once per activation. A failed load must never be
    /// mistaken for "this player has no progress": writing on top of that empty map would reset
    /// every counter to zero. The flag therefore stays false so the next call retries, and callers
    /// that would write check the result first.
    /// </summary>
    private async Task<bool> EnsureHydratedAsync(CancellationToken ct)
    {
        if (_hydrated)
        {
            return true;
        }

        try
        {
            await using VortexDbContext dbCtx = await _dbCtxFactory
                .CreateDbContextAsync(ct)
                .ConfigureAwait(true);

            List<PlayerAchievementEntity> rows = await dbCtx
                .PlayerAchievements.AsNoTracking()
                .Where(p => p.PlayerEntityId == PlayerId)
                .ToListAsync(ct)
                .ConfigureAwait(true);

            _stateByAchievementId.Clear();

            foreach (PlayerAchievementEntity row in rows)
            {
                _stateByAchievementId[row.AchievementEntityId] = new ProgressState
                {
                    EntityId = row.Id,
                    Progress = row.Progress,
                    CompletedLevels = row.Level,
                    LastProgressAt = row.UpdatedAt,
                };
            }

            _hydrated = true;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to load achievement progress for player {PlayerId}",
                PlayerId
            );
            return false;
        }
    }

    private PlayerAchievementEntity BuildRow(
        int achievementId,
        int entityId,
        int progress,
        int completedLevels
    ) =>
        new()
        {
            Id = entityId,
            PlayerEntityId = PlayerId,
            AchievementEntityId = achievementId,
            Progress = progress,
            Level = completedLevels,
        };

    /// <summary>
    /// Attaches an existing row by id and marks only the columns this write owns, so an update costs
    /// no read first.
    /// </summary>
    private static void TrackProgressUpdate(
        VortexDbContext dbCtx,
        PlayerAchievementEntity row,
        bool includeLevel
    )
    {
        dbCtx.Attach(row);

        EntityEntry<PlayerAchievementEntity> entry = dbCtx.Entry(row);
        entry.Property(x => x.Progress).IsModified = true;

        if (includeLevel)
        {
            entry.Property(x => x.Level).IsModified = true;
        }
    }
}
