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
using Vortex.Primitives.Commerce;
using Vortex.Primitives.Events;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Players.Snapshots;
using Vortex.Primitives.Players.Wallet;
using Vortex.Primitives.Quests;
using Vortex.Primitives.Quests.Grains;
using Vortex.Primitives.Quests.Snapshots;
using Vortex.Primitives.Server.Grains;
using Vortex.Progression.Quests;
using Vortex.Protocol.Messages.Outgoing.Quest;

namespace Vortex.Progression.Grains;

/// <summary>
/// Per-player quest grain. Resolves the player's stored progress rows against the cached quest
/// definitions into wire-ready snapshots, and owns quest lifecycle (activate/accept/cancel/reject)
/// and objective progression (with completion rewards).
/// </summary>
internal sealed class PlayerQuestGrain(
    IGrainFactory grainFactory,
    IDbContextFactory<VortexDbContext> dbCtxFactory,
    IEventPublisher events,
    ICommerceJournal journal,
    ILogger<PlayerQuestGrain> logger
) : Grain, IPlayerQuestGrain
{
    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly IDbContextFactory<VortexDbContext> _dbCtxFactory = dbCtxFactory;
    private readonly IEventPublisher _events = events;
    private readonly ICommerceJournal _journal = journal;
    private readonly ILogger<PlayerQuestGrain> _logger = logger;

    /// <summary>Campaign whose quests form the rotating daily pool (shown via the daily section, not
    /// the main campaign list).</summary>
    private const string DailyCampaign = "daily";

    /// <summary>Admin-editable knobs for the landing-view "players online" goal.</summary>
    private const string ConcurrentUsersGoalEnabledKey = "quests.concurrent_users_goal.enabled";
    private const string ConcurrentUsersGoalCountKey = "quests.concurrent_users_goal.user_count";
    private const string ConcurrentUsersGoalBadgeKey = "quests.concurrent_users_goal.reward_badge";

    private int PlayerId => (int)this.GetPrimaryKeyLong();

    public async Task<QuestListSnapshot> GetQuestsAsync(bool openWindow, CancellationToken ct)
    {
        ImmutableArray<QuestSnapshot> all = await BuildAllAsync(ct).ConfigureAwait(true);

        return new QuestListSnapshot { Quests = BuildListQuests(all), OpenWindow = openWindow };
    }

    public async Task SendConcurrentUsersGoalAsync(int onlineCount, CancellationToken ct)
    {
        (bool enabled, int goal, string badgeCode) = await ReadConcurrentGoalConfigAsync()
            .ConfigureAwait(true);

        bool rewarded = await HasBadgeAsync(badgeCode, ct).ConfigureAwait(true);

        await SendConcurrentUsersGoalAsync(
                ConcurrentUsersGoalRule.Resolve(enabled, goal, onlineCount, rewarded),
                onlineCount,
                goal
            )
            .ConfigureAwait(true);
    }

    public async Task ClaimConcurrentUsersRewardAsync(int onlineCount, CancellationToken ct)
    {
        (bool enabled, int goal, string badgeCode) = await ReadConcurrentGoalConfigAsync()
            .ConfigureAwait(true);

        bool rewarded = await HasBadgeAsync(badgeCode, ct).ConfigureAwait(true);

        if (ConcurrentUsersGoalRule.CanClaim(enabled, goal, onlineCount, rewarded))
        {
            await _grainFactory
                .GetInventoryGrain(PlayerId)
                .GrantBadgeAsync(badgeCode, ct)
                .ConfigureAwait(true);

            rewarded = true;

            _logger.LogInformation(
                "Player {PlayerId} claimed the concurrent-users goal reward ({BadgeCode}) at {OnlineCount} online.",
                PlayerId,
                badgeCode,
                onlineCount
            );
        }

        // Answered either way: a refused claim (too few online, or a second click) must still leave
        // the widget showing the truth rather than spinning on the button it just pressed.
        await SendConcurrentUsersGoalAsync(
                ConcurrentUsersGoalRule.Resolve(enabled, goal, onlineCount, rewarded),
                onlineCount,
                goal
            )
            .ConfigureAwait(true);
    }

    private Task SendConcurrentUsersGoalAsync(
        ConcurrentUsersGoalState state,
        int onlineCount,
        int goal
    ) =>
        Presence.SendComposerAsync(
            new ConcurrentUsersGoalProgressMessageComposer
            {
                State = state,
                UserCount = onlineCount,
                UserCountGoal = goal,
            }
        );

    /// <summary>
    /// The goal's admin-editable knobs. They live in the server-config grain rather than appsettings
    /// so an operator can raise the target mid-event without a restart.
    /// </summary>
    private async Task<(bool Enabled, int Goal, string BadgeCode)> ReadConcurrentGoalConfigAsync()
    {
        IServerConfigGrain config = _grainFactory.GetServerConfigGrain();

        bool enabled = await config
            .GetBoolAsync(ConcurrentUsersGoalEnabledKey, false)
            .ConfigureAwait(true);
        int goal = await config.GetIntAsync(ConcurrentUsersGoalCountKey, 0).ConfigureAwait(true);
        string badgeCode =
            await config.GetValueAsync(ConcurrentUsersGoalBadgeKey).ConfigureAwait(true)
            ?? string.Empty;

        // No badge configured means there is nothing to hand out, so the goal is not really on --
        // otherwise the button would claim a reward that does not exist and never flip to rewarded.
        return (enabled && badgeCode.Length > 0, goal, badgeCode);
    }

    private async Task<bool> HasBadgeAsync(string badgeCode, CancellationToken ct)
    {
        if (badgeCode.Length == 0)
        {
            return false;
        }

        ImmutableArray<PlayerBadgeSnapshot> badges = await _grainFactory
            .GetPlayerBadgeGrain(PlayerId)
            .GetBadgesAsync(ct)
            .ConfigureAwait(true);

        return badges.Any(b => string.Equals(b.BadgeCode, badgeCode, StringComparison.Ordinal));
    }

    public async Task StartCampaignAsync(string campaignCode, CancellationToken ct)
    {
        ImmutableArray<QuestSnapshot> all = await BuildAllAsync(ct).ConfigureAwait(true);

        // The window opens on the full list -- the client renders every campaign and highlights the
        // one it asked for, so answering with the campaign alone would empty the rest of the window.
        await Presence
            .SendComposerAsync(
                new QuestsMessageComposer { Quests = BuildListQuests(all), OpenWindow = true }
            )
            .ConfigureAwait(true);

        List<QuestSnapshot> campaign =
        [
            .. all.Where(q =>
                !q.Seasonal
                && q.CampaignCode.Equals(campaignCode, StringComparison.OrdinalIgnoreCase)
            ),
        ];

        if (campaign.Count == 0)
        {
            _logger.LogDebug(
                "Player {PlayerId} asked to start campaign {CampaignCode}, which has no quests.",
                PlayerId,
                campaignCode
            );

            return;
        }

        // The tracker sets "open details for the next quest" just before sending this, so it wants
        // the campaign's current quest pushed as well. Deliberately NOT accepted here: accepting is
        // the player's own click, and auto-accepting would start a timer they never asked for.
        QuestSnapshot current = SelectVisibleQuest(campaign.GroupBy(q => q.CampaignCode).First());

        await Presence
            .SendComposerAsync(new QuestMessageComposer { Quest = current })
            .ConfigureAwait(true);
    }

    public async Task<QuestListSnapshot> GetSeasonalQuestsAsync(CancellationToken ct)
    {
        ImmutableArray<QuestSnapshot> all = await BuildAllAsync(ct).ConfigureAwait(true);

        return new QuestListSnapshot
        {
            Quests = [.. all.Where(q => q.Seasonal)],
            OpenWindow = false,
        };
    }

    public async Task<DailyQuestSnapshot> GetDailyQuestAsync(CancellationToken ct)
    {
        ImmutableArray<QuestSnapshot> all = await BuildAllAsync(ct).ConfigureAwait(true);

        List<QuestSnapshot> dailyPool =
        [
            .. all.Where(q => q.CampaignCode == DailyCampaign && !q.Seasonal).OrderBy(q => q.Id),
        ];

        QuestSnapshot? daily = DailyQuestSelector.Pick(
            dailyPool,
            PlayerId,
            DateOnly.FromDateTime(DateTime.UtcNow)
        );

        return new DailyQuestSnapshot
        {
            Quest = daily,
            EasyQuestCount = dailyPool.Count(q => q.Easy),
            HardQuestCount = dailyPool.Count(q => !q.Easy),
        };
    }

    public async Task<QuestSnapshot?> GetTrackedQuestAsync(CancellationToken ct)
    {
        ImmutableArray<QuestSnapshot> all = await BuildAllAsync(ct).ConfigureAwait(true);

        return all.FirstOrDefault(q => q.Accepted && q.CompletedSteps < q.TotalSteps);
    }

    public Task AcceptAsync(int questId, CancellationToken ct) => ActivateAsync(questId, ct);

    public async Task ActivateAsync(int questId, CancellationToken ct)
    {
        List<int> deactivatedQuestIds = [];

        try
        {
            await using VortexDbContext dbCtx = await _dbCtxFactory
                .CreateDbContextAsync(ct)
                .ConfigureAwait(true);

            List<PlayerQuestEntity> rows = await dbCtx
                .PlayerQuests.Where(p => p.PlayerEntityId == PlayerId)
                .ToListAsync(ct)
                .ConfigureAwait(true);

            PlayerQuestEntity? row = rows.FirstOrDefault(r => r.QuestEntityId == questId);

            // An already-completed quest has nothing left to activate - bail before touching any
            // other in-progress quest. Without this, a stale "Activate" click on a finished quest
            // (still on screen from before the list refreshed) would deactivate whatever else was
            // genuinely in progress for no reason, and re-push the finished quest's own now-stale
            // single-quest update - re-showing/refreshing its tracker as if newly active instead of
            // leaving the already-played completion animation alone.
            if (row is { Completed: true })
            {
                return;
            }

            // One active quest at a time: deactivate any other in-progress quest.
            foreach (
                PlayerQuestEntity other in rows.Where(r =>
                    r.QuestEntityId != questId && r.Accepted && !r.Completed
                )
            )
            {
                other.Accepted = false;
                deactivatedQuestIds.Add(other.QuestEntityId);
            }

            if (row is null)
            {
                dbCtx.PlayerQuests.Add(
                    new PlayerQuestEntity
                    {
                        PlayerEntityId = PlayerId,
                        QuestEntityId = questId,
                        Accepted = true,
                        AcceptedAt = DateTime.UtcNow,
                    }
                );
            }
            else
            {
                row.Accepted = true;
                row.AcceptedAt = DateTime.UtcNow;
            }

            await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to activate quest {QuestId} for player {PlayerId}",
                questId,
                PlayerId
            );
            return;
        }

        ImmutableArray<QuestSnapshot> all = await BuildAllAsync(ct).ConfigureAwait(true);
        Dictionary<int, QuestSnapshot> byId = all.ToDictionary(q => q.Id);

        // Tell the client about every quest the "one active quest at a time" rule above just
        // deactivated, *before* the newly-activated quest's own update. A same-campaign quest
        // shares one QuestTracker client-side (campaignChainCode is just the campaign code for
        // non-seasonal quests) - without this, the tracker never receives the cancellation signal
        // that resets its animation state (AS3's own onQuest() only resets from the SLIDE_OUT
        // status this produces), and can get stuck mid a stale completion animation instead of
        // cleanly transitioning to the newly-activated quest.
        foreach (int deactivatedId in deactivatedQuestIds)
        {
            if (byId.TryGetValue(deactivatedId, out QuestSnapshot? deactivatedSnapshot))
            {
                await Presence
                    .SendComposerAsync(
                        new QuestCancelledMessageComposer
                        {
                            Expired = false,
                            Quest = deactivatedSnapshot,
                        }
                    )
                    .ConfigureAwait(true);
            }
        }

        if (byId.TryGetValue(questId, out QuestSnapshot? snapshot))
        {
            await Presence
                .SendComposerAsync(new QuestMessageComposer { Quest = snapshot })
                .ConfigureAwait(true);

            await _events
                .PublishAsync(
                    new QuestAcceptedEvent(
                        PlayerId,
                        snapshot.Id,
                        snapshot.CampaignCode,
                        snapshot.LocalizationCode
                    ),
                    ct
                )
                .ConfigureAwait(true);
        }

        // The single-quest update above is what drives the tracker/details/completed popups,
        // but QuestsList (the main quest window, if already open) only ever refreshes off a full
        // list push - without this, a quest accepted while the list is open never shows its
        // "accepted" (yellow) state until the window is closed and reopened. Reuses the snapshot
        // array already built above instead of re-querying.
        await Presence
            .SendComposerAsync(
                new QuestsMessageComposer { Quests = BuildListQuests(all), OpenWindow = false }
            )
            .ConfigureAwait(true);
    }

    public Task CancelAsync(CancellationToken ct) => UnacceptAsync(questId: null, ct);

    public Task RejectAsync(int questId, CancellationToken ct) => UnacceptAsync(questId, ct);

    public Task ProgressAsync(string questType, int amount, CancellationToken ct) =>
        ProgressCoreAsync(questType, amount, null, null, oncePerDay: false, ct);

    public Task ProgressAsync(
        string questType,
        int amount,
        string? targetType,
        string? targetValue,
        CancellationToken ct
    ) => ProgressCoreAsync(questType, amount, targetType, targetValue, oncePerDay: false, ct);

    public Task ProgressDailyAsync(string questType, int amount, CancellationToken ct) =>
        ProgressCoreAsync(questType, amount, null, null, oncePerDay: true, ct);

    public async Task ProgressRoomVisitAsync(
        int roomId,
        DateTime enteredAtUtc,
        CancellationToken ct
    )
    {
        // "Visit N different rooms": skip the player's own room and count each room only once. Both
        // checks reuse existing data (the rooms table + the room-entry log the observability handler
        // already writes for every entry), so there is no dedup table. Re-entering a known room hits
        // these two cheap indexed reads and early-exits before the heavier progression runs.
        try
        {
            await using VortexDbContext dbCtx = await _dbCtxFactory
                .CreateDbContextAsync(ct)
                .ConfigureAwait(true);

            int? ownerId = await dbCtx
                .Rooms.AsNoTracking()
                .Where(r => r.Id == roomId)
                .Select(r => (int?)r.PlayerEntityId)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(true);

            if (ownerId is null || ownerId == PlayerId)
            {
                return;
            }

            // Any earlier entry (strictly before this one) means the room was already counted. The
            // timestamp guard makes this robust to the log writer running before or after us for the
            // current entry, since it shares the same PlayerEnteredRoomEvent.
            bool visitedBefore = await dbCtx
                .RoomEntryLogs.AsNoTracking()
                .AnyAsync(
                    l =>
                        l.PlayerEntityId == PlayerId
                        && l.RoomEntityId == roomId
                        && l.CreatedAt < enteredAtUtc,
                    ct
                )
                .ConfigureAwait(true);

            if (visitedBefore)
            {
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed the room-visit dedup check for room {RoomId}, player {PlayerId}",
                roomId,
                PlayerId
            );
            return;
        }

        await ProgressCoreAsync(QuestTypes.RoomEntry, 1, null, null, oncePerDay: false, ct)
            .ConfigureAwait(true);
    }

    private async Task ProgressCoreAsync(
        string questType,
        int amount,
        string? targetType,
        string? targetValue,
        bool oncePerDay,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(questType) || amount <= 0)
        {
            return;
        }

        ImmutableArray<QuestDefinitionSnapshot> definitions = await _grainFactory
            .GetQuestManagerGrain()
            .GetDefinitionsAsync(ct)
            .ConfigureAwait(true);

        // A quest with no configured target advances on any occurrence of its type; a targeted quest
        // only advances when the trigger supplies a matching target (e.g. "buy offer 12").
        Dictionary<int, QuestDefinitionSnapshot> defByType = definitions
            .Where(d =>
                string.Equals(d.QuestType, questType, StringComparison.OrdinalIgnoreCase)
                && (
                    string.IsNullOrEmpty(d.TargetType)
                    || (
                        string.Equals(d.TargetType, targetType, StringComparison.Ordinal)
                        && string.Equals(d.TargetValue, targetValue, StringComparison.Ordinal)
                    )
                )
            )
            .ToDictionary(d => d.Id);

        if (defByType.Count == 0)
        {
            return;
        }

        // Today's daily quest (of this type) is auto-active: it progresses without being manually
        // accepted, so resolve which one it is for this player and day.
        int? dailyQuestId = ResolveTodaysDailyOfType(definitions, defByType);

        List<(int QuestId, bool Completed)> changed = [];

        try
        {
            await using VortexDbContext dbCtx = await _dbCtxFactory
                .CreateDbContextAsync(ct)
                .ConfigureAwait(true);

            List<int> ids = [.. defByType.Keys];
            List<PlayerQuestEntity> existing = await dbCtx
                .PlayerQuests.Where(p =>
                    p.PlayerEntityId == PlayerId && ids.Contains(p.QuestEntityId)
                )
                .ToListAsync(ct)
                .ConfigureAwait(true);

            // Quests to advance: accepted campaign quests, plus today's daily (auto-active).
            List<PlayerQuestEntity> toProcess =
            [
                .. existing.Where(r => r.Accepted && !r.Completed),
            ];

            if (dailyQuestId is int did)
            {
                PlayerQuestEntity? dailyRow = existing.FirstOrDefault(r => r.QuestEntityId == did);
                if (dailyRow is null)
                {
                    dailyRow = new PlayerQuestEntity
                    {
                        PlayerEntityId = PlayerId,
                        QuestEntityId = did,
                        Accepted = true,
                        AcceptedAt = DateTime.UtcNow,
                    };
                    dbCtx.PlayerQuests.Add(dailyRow);
                    toProcess.Add(dailyRow);
                }
                else if (!dailyRow.Completed && !toProcess.Contains(dailyRow))
                {
                    toProcess.Add(dailyRow);
                }
            }

            // Once-per-day objectives (e.g. Login) advance at most once per calendar day: a row's
            // last-write date is when it last progressed, so skip any that already progressed today
            // (a freshly-created row has a default date and still counts as its first time today).
            if (oncePerDay)
            {
                DateTime today = DateTime.UtcNow.Date;
                toProcess = [.. toProcess.Where(r => r.UpdatedAt.Date != today)];
            }

            if (toProcess.Count == 0)
            {
                return;
            }

            foreach (PlayerQuestEntity row in toProcess)
            {
                QuestDefinitionSnapshot def = defByType[row.QuestEntityId];
                (int newSteps, bool done) = QuestProgressCalculator.Apply(
                    row.CompletedSteps,
                    amount,
                    def.TotalSteps
                );
                row.CompletedSteps = newSteps;
                row.Completed = done;
                if (done && row.CompletedAt is null)
                {
                    row.CompletedAt = DateTime.UtcNow;
                }

                changed.Add((row.QuestEntityId, done));
            }

            await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to progress quests of type {Type} for player {PlayerId}",
                questType,
                PlayerId
            );
            return;
        }

        ImmutableArray<QuestSnapshot> all = await BuildAllAsync(ct).ConfigureAwait(true);
        Dictionary<int, QuestSnapshot> byId = all.ToDictionary(q => q.Id);

        foreach ((int questId, bool done) in changed)
        {
            if (!byId.TryGetValue(questId, out QuestSnapshot? snapshot))
            {
                continue;
            }

            if (done)
            {
                await GrantRewardAsync(
                        snapshot.Id,
                        snapshot.ActivityPointType,
                        snapshot.RewardCurrencyAmount,
                        ct
                    )
                    .ConfigureAwait(true);
                await Presence
                    .SendComposerAsync(
                        new QuestCompletedMessageComposer { Quest = snapshot, ShowDialog = true }
                    )
                    .ConfigureAwait(true);

                // A finished quest feeds the community goal, if the active one is fed by this
                // campaign. The goal grain decides that -- it owns which campaign counts, what a
                // completion is worth (`score_per_quest`), and ignores everything else. One quest
                // finished is one completion here; turning it into score is not this grain's call.
                await _grainFactory
                    .GetCommunityGoalGrain()
                    .ContributeAsync(PlayerId, snapshot.CampaignCode, completions: 1, ct)
                    .ConfigureAwait(true);

                await _events
                    .PublishAsync(
                        new QuestCompletedEvent(
                            PlayerId,
                            snapshot.Id,
                            snapshot.CampaignCode,
                            snapshot.LocalizationCode,
                            snapshot.ActivityPointType,
                            snapshot.RewardCurrencyAmount
                        ),
                        ct
                    )
                    .ConfigureAwait(true);
            }
            else
            {
                await Presence
                    .SendComposerAsync(new QuestMessageComposer { Quest = snapshot })
                    .ConfigureAwait(true);
            }
        }

        // See ActivateAsync's matching comment - refresh the still-open QuestsList (if any) once
        // for the whole batch, reusing the snapshot array already built above.
        await Presence
            .SendComposerAsync(
                new QuestsMessageComposer { Quests = BuildListQuests(all), OpenWindow = false }
            )
            .ConfigureAwait(true);
    }

    /// <summary>
    /// Resolves this player's daily quest for today and returns its id only if its type is among
    /// <paramref name="defByType"/> (i.e. the current trigger can advance it); otherwise null.
    /// </summary>
    private int? ResolveTodaysDailyOfType(
        ImmutableArray<QuestDefinitionSnapshot> definitions,
        Dictionary<int, QuestDefinitionSnapshot> defByType
    )
    {
        List<QuestDefinitionSnapshot> dailyDefs =
        [
            .. definitions.Where(d => d.CampaignCode == DailyCampaign).OrderBy(d => d.Id),
        ];

        int index = DailyQuestSelector.PickIndex(
            dailyDefs.Count,
            PlayerId,
            DateOnly.FromDateTime(DateTime.UtcNow)
        );

        if (index < 0)
        {
            return null;
        }

        int questId = dailyDefs[index].Id;
        return defByType.ContainsKey(questId) ? questId : null;
    }

    private async Task UnacceptAsync(int? questId, CancellationToken ct)
    {
        int targetQuestId;

        try
        {
            await using VortexDbContext dbCtx = await _dbCtxFactory
                .CreateDbContextAsync(ct)
                .ConfigureAwait(true);

            PlayerQuestEntity? row = questId is int id
                ? await dbCtx
                    .PlayerQuests.FirstOrDefaultAsync(
                        p => p.PlayerEntityId == PlayerId && p.QuestEntityId == id,
                        ct
                    )
                    .ConfigureAwait(true)
                // No id (client "cancel"): the single active, in-progress quest.
                : await dbCtx
                    .PlayerQuests.Where(p =>
                        p.PlayerEntityId == PlayerId && p.Accepted && !p.Completed
                    )
                    .OrderByDescending(p => p.AcceptedAt)
                    .FirstOrDefaultAsync(ct)
                    .ConfigureAwait(true);

            if (row is null || !row.Accepted)
            {
                return;
            }

            row.Accepted = false;
            targetQuestId = row.QuestEntityId;

            await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel quest for player {PlayerId}", PlayerId);
            return;
        }

        ImmutableArray<QuestSnapshot> all = await BuildAllAsync(ct).ConfigureAwait(true);
        QuestSnapshot? snapshot = all.FirstOrDefault(q => q.Id == targetQuestId);

        if (snapshot is not null)
        {
            await Presence
                .SendComposerAsync(
                    new QuestCancelledMessageComposer { Expired = false, Quest = snapshot }
                )
                .ConfigureAwait(true);

            await _events
                .PublishAsync(
                    new QuestAbandonedEvent(
                        PlayerId,
                        snapshot.Id,
                        snapshot.CampaignCode,
                        snapshot.LocalizationCode,
                        Rejected: questId is not null
                    ),
                    ct
                )
                .ConfigureAwait(true);
        }

        // See ActivateAsync's matching comment - QuestsList only refreshes off a full list push.
        await Presence
            .SendComposerAsync(
                new QuestsMessageComposer { Quests = BuildListQuests(all), OpenWindow = false }
            )
            .ConfigureAwait(true);
    }

    private async Task GrantRewardAsync(
        int questId,
        int rewardType,
        int rewardAmount,
        CancellationToken ct
    )
    {
        if (rewardAmount <= 0)
        {
            return;
        }

        IPlayerWalletGrain wallet = _grainFactory.GetPlayerWalletGrain((long)PlayerId);

        // The quest row was marked complete and saved before this ran, which is the right order and
        // means the reward is owed from that moment. Under an operation so that a payout that never
        // lands is findable instead of silent (PROG-REWARD-032).
        await _journal
            .RecordOwedPayoutAsync(
                CommerceOperationKind.QuestReward,
                (int)PlayerId,
                $"quest={questId} amount={rewardAmount} type={rewardType}",
                innerCt =>
                    wallet.GrantCurrencyAsync(
                        CurrencyRewardRules.KindFor(rewardType),
                        rewardAmount,
                        innerCt
                    ),
                _logger,
                ct
            )
            .ConfigureAwait(true);
    }

    /// <summary>
    /// The quest list shows exactly one row per campaign - the client (QuestsList.as::onQuests())
    /// renders every row it is sent with no campaign-collapsing of its own, so the server is what's
    /// responsible for picking which single quest represents each campaign. Mirrors the reference
    /// Arcturus-Daybreak emulator's QuestManager.getVisibleQuestFromList(): the in-progress quest if
    /// one is accepted, else the earliest not-yet-completed quest, else (whole campaign done) the
    /// last quest in sort order - never more than one row per campaign. Without this, every quest a
    /// campaign has ever had (completed ones included) was sent as its own row, all sharing the same
    /// campaign name/counter - which is exactly the "duplicate Make Friends rows stuck at 100%" bug.
    /// Shared so callers that already have a full snapshot array (e.g. after mutating one quest) can
    /// push a fresh QuestsMessageComposer without re-querying.
    /// </summary>
    private static ImmutableArray<QuestSnapshot> BuildListQuests(
        ImmutableArray<QuestSnapshot> all
    ) =>
        [
            .. all.Where(q => !q.Seasonal && q.CampaignCode != DailyCampaign)
                .GroupBy(q => q.CampaignCode)
                .Select(SelectVisibleQuest)
                .OrderBy(q => q.SortOrder)
                .ThenBy(q => q.Id),
        ];

    private static QuestSnapshot SelectVisibleQuest(IGrouping<string, QuestSnapshot> campaignQuests)
    {
        List<QuestSnapshot> ordered =
        [
            .. campaignQuests.OrderBy(q => q.SortOrder).ThenBy(q => q.Id),
        ];

        QuestSnapshot representative = ordered[^1];

        // Whole campaign completed: emit the id=0 "campaign completed" marker (à la Arcturus) instead
        // of re-showing the last already-completed quest as if it were current. The client reads id < 1
        // as "this campaign is finished" and renders the completed state.
        if (
            representative.QuestCountInCampaign > 0
            && representative.CompletedQuestsInCampaign >= representative.QuestCountInCampaign
        )
        {
            return CompletedCampaignMarker(representative);
        }

        return ordered.FirstOrDefault(q => q.Accepted && q.CompletedSteps < q.TotalSteps)
            ?? ordered.FirstOrDefault(q => q.CompletedSteps < q.TotalSteps)
            ?? representative;
    }

    /// <summary>
    /// The "campaign completed" list entry: keeps the campaign identity + progress counts but blanks
    /// the objective and uses <c>Id = 0</c>, which the client (like Arcturus) reads as "campaign done".
    /// </summary>
    private static QuestSnapshot CompletedCampaignMarker(QuestSnapshot representative) =>
        new()
        {
            CampaignCode = representative.CampaignCode,
            CompletedQuestsInCampaign = representative.CompletedQuestsInCampaign,
            QuestCountInCampaign = representative.QuestCountInCampaign,
            ActivityPointType = representative.ActivityPointType,
            Id = 0,
            Accepted = false,
            QuestType = string.Empty,
            ImageVersion = string.Empty,
            RewardCurrencyAmount = 0,
            LocalizationCode = string.Empty,
            CompletedSteps = 0,
            TotalSteps = 0,
            SortOrder = representative.SortOrder,
            CatalogPageName = string.Empty,
            ChainCode = representative.ChainCode,
            Easy = false,
            Seasonal = false,
            SecondsLeft = 0,
        };

    private IPlayerPresenceGrain Presence => _grainFactory.GetPlayerPresenceGrain((long)PlayerId);

    private async Task<ImmutableArray<QuestSnapshot>> BuildAllAsync(CancellationToken ct)
    {
        ImmutableArray<QuestDefinitionSnapshot> definitions = await _grainFactory
            .GetQuestManagerGrain()
            .GetDefinitionsAsync(ct)
            .ConfigureAwait(true);

        if (definitions.IsEmpty)
        {
            return ImmutableArray<QuestSnapshot>.Empty;
        }

        Dictionary<int, PlayerQuestEntity> progressByQuest = await LoadProgressAsync(ct)
            .ConfigureAwait(true);

        // Campaign aggregates: how many quests each campaign has, and how many the player completed.
        Dictionary<string, int> questCountByCampaign = definitions
            .GroupBy(d => d.CampaignCode)
            .ToDictionary(g => g.Key, g => g.Count());

        Dictionary<string, int> completedByCampaign = definitions
            .Where(d => progressByQuest.TryGetValue(d.Id, out PlayerQuestEntity? p) && p.Completed)
            .GroupBy(d => d.CampaignCode)
            .ToDictionary(g => g.Key, g => g.Count());

        ImmutableArray<QuestSnapshot>.Builder builder = ImmutableArray.CreateBuilder<QuestSnapshot>(
            definitions.Length
        );

        foreach (QuestDefinitionSnapshot definition in definitions)
        {
            progressByQuest.TryGetValue(definition.Id, out PlayerQuestEntity? progress);
            completedByCampaign.TryGetValue(definition.CampaignCode, out int completedInCampaign);

            builder.Add(
                QuestSnapshotBuilder.Build(
                    definition,
                    progress?.CompletedSteps ?? 0,
                    progress?.Accepted ?? false,
                    progress?.Completed ?? false,
                    completedInCampaign,
                    questCountByCampaign.GetValueOrDefault(definition.CampaignCode),
                    DateTime.UtcNow
                )
            );
        }

        return builder.ToImmutable();
    }

    private async Task<Dictionary<int, PlayerQuestEntity>> LoadProgressAsync(CancellationToken ct)
    {
        try
        {
            await using VortexDbContext dbCtx = await _dbCtxFactory
                .CreateDbContextAsync(ct)
                .ConfigureAwait(true);

            List<PlayerQuestEntity> rows = await dbCtx
                .PlayerQuests.AsNoTracking()
                .Where(p => p.PlayerEntityId == PlayerId)
                .ToListAsync(ct)
                .ConfigureAwait(true);

            return rows.ToDictionary(r => r.QuestEntityId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load quest progress for player {PlayerId}", PlayerId);
            return [];
        }
    }
}
