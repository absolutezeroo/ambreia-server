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
using Vortex.Primitives.Players.Wallet;
using Vortex.Primitives.Quests;
using Vortex.Primitives.Quests.Grains;
using Vortex.Primitives.Quests.Snapshots;
using Vortex.Progression.Quests;
using Vortex.Protocol.Messages.Outgoing.Quest;

namespace Vortex.Progression.Grains;

/// <summary>
/// Per-player daily tasks. Draws the day's board on first read, advances it from the same domain
/// events quests use, and hands over rewards on claim.
/// </summary>
internal sealed class PlayerDailyTaskGrain(
    IGrainFactory grainFactory,
    IDbContextFactory<VortexDbContext> dbCtxFactory,
    IEventPublisher events,
    ILogger<PlayerDailyTaskGrain> logger,
    ICommerceJournal journal
) : Grain, IPlayerDailyTaskGrain
{
    private readonly ICommerceJournal _journal = journal;
    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly IDbContextFactory<VortexDbContext> _dbCtxFactory = dbCtxFactory;
    private readonly IEventPublisher _events = events;
    private readonly ILogger<PlayerDailyTaskGrain> _logger = logger;

    /// <summary>The reward kind granted as credits; anything else is an activity-point type.</summary>
    private int PlayerId => (int)this.GetPrimaryKeyLong();

    private IPlayerPresenceGrain Presence => _grainFactory.GetPlayerPresenceGrain(PlayerId);

    public async Task SendBoardAsync(int taskCount, int bonusCount, CancellationToken ct)
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(true);

        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);

        List<PlayerDailyTaskEntity> assignments = await LoadAssignmentsAsync(dbCtx, today, ct)
            .ConfigureAwait(true);

        if (assignments.Count == 0)
        {
            assignments = await DrawBoardAsync(dbCtx, today, taskCount, bonusCount, ct)
                .ConfigureAwait(true);
        }

        ImmutableArray<DailyTaskSnapshot> tasks = await BuildSnapshotsAsync(dbCtx, assignments, ct)
            .ConfigureAwait(true);

        await Presence
            .SendComposerAsync(new DailyTasksActiveListMessageComposer { Tasks = tasks })
            .ConfigureAwait(true);
    }

    public async Task ProgressAsync(string questTypeCode, int amount, CancellationToken ct)
    {
        if (amount <= 0 || string.IsNullOrWhiteSpace(questTypeCode))
        {
            return;
        }

        List<(int TaskId, int Repeats, DailyTaskStatus Status, int SecondsLeft)> changed = [];

        try
        {
            await using VortexDbContext dbCtx = await _dbCtxFactory
                .CreateDbContextAsync(ct)
                .ConfigureAwait(true);

            DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
            DateTime now = DateTime.UtcNow;

            List<PlayerDailyTaskEntity> assignments = await dbCtx
                .PlayerDailyTasks.Include(a => a.DailyTaskEntity)
                .Where(a =>
                    a.PlayerEntityId == PlayerId
                    && a.AssignedOn == today
                    && a.Status == DailyTaskStatus.Available
                    && a.ExpiresAt > now
                    && a.DailyTaskEntity != null
                    && a.DailyTaskEntity.QuestTypeCode == questTypeCode
                )
                .ToListAsync(ct)
                .ConfigureAwait(true);

            if (assignments.Count == 0)
            {
                return;
            }

            foreach (PlayerDailyTaskEntity assignment in assignments)
            {
                int required = Math.Max(1, assignment.DailyTaskEntity!.RequiredRepeats);

                assignment.Repeats = Math.Min(required, assignment.Repeats + amount);

                if (assignment.Repeats >= required)
                {
                    assignment.Status = DailyTaskStatus.Completed;
                }

                changed.Add(
                    (
                        assignment.Id,
                        assignment.Repeats,
                        assignment.Status,
                        SecondsLeft(assignment.ExpiresAt)
                    )
                );
            }

            await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to progress daily tasks of type {QuestTypeCode} for player {PlayerId}.",
                questTypeCode,
                PlayerId
            );

            return;
        }

        foreach ((int taskId, int repeats, DailyTaskStatus status, int secondsLeft) in changed)
        {
            await Presence
                .SendComposerAsync(
                    new DailyTasksTaskUpdateMessageComposer
                    {
                        TaskId = taskId,
                        Repeats = repeats,
                        Status = status,
                        SecondsLeft = secondsLeft,
                    }
                )
                .ConfigureAwait(true);
        }
    }

    public async Task ClaimAsync(int taskId, CancellationToken ct)
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(true);

        PlayerDailyTaskEntity? assignment = await dbCtx
            .PlayerDailyTasks.FirstOrDefaultAsync(
                a => a.Id == taskId && a.PlayerEntityId == PlayerId,
                ct
            )
            .ConfigureAwait(true);

        // Only a completed task pays out, and only once. A second click, someone else's task id, or
        // a task that lapsed before it was claimed all land here.
        if (assignment is null || assignment.Status != DailyTaskStatus.Completed)
        {
            return;
        }

        List<DailyTaskRewardEntity> rewards = await dbCtx
            .DailyTaskRewards.AsNoTracking()
            .Where(r => r.DailyTaskEntityId == assignment.DailyTaskEntityId)
            .ToListAsync(ct)
            .ConfigureAwait(true);

        // Derived from the assignment rather than minted fresh: a player clicking claim twice, or a
        // client retrying, is the same operation asking again and not a second payout.
        CommerceOperationId operation = CommerceOperationId.Deterministic(
            CommerceOperationKind.DailyTaskReward,
            assignment.Id
        );

        await _journal
            .OpenIfNewAsync(
                operation,
                CommerceOperationKind.DailyTaskReward,
                (int)PlayerId,
                $"daily task={assignment.DailyTaskEntityId} assignment={assignment.Id}",
                ct
            )
            .ConfigureAwait(true);

        assignment.Status = DailyTaskStatus.Claimed;
        assignment.ClaimedAt = DateTime.UtcNow;

        // The status is committed before the payout so a failure mid-grant cannot leave a task
        // claimable twice.
        await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);

        // THE PIVOT, and it is why this needed a journal at all. Committing the claim first is the
        // right order -- a task that pays twice is worse than one that pays late -- but it means
        // everything after this point is owed. A throw in the payout below used to end the story:
        // the task read as claimed, nothing was paid, and the only trace was whatever exception
        // happened to be logged upstream. Now the operation sits at Pivoted, and
        // CommerceRelayService.EscalateAsync turns it into a critical naming the player and the
        // step once it has been stuck past its deadline.
        await _journal
            .TransitionAsync(
                operation,
                CommerceOperationState.Pivoted,
                CommerceStepKeys.REWARD_PAYOUT,
                null,
                ct
            )
            .ConfigureAwait(true);

        bool delivered = true;

        foreach (DailyTaskRewardEntity reward in rewards)
        {
            delivered &= await GrantAsync(reward, operation, ct).ConfigureAwait(true);
        }

        // Completed only when the whole board went out. A reward this build cannot hand over --
        // today, anything that is not a currency -- used to be logged and then sealed under a
        // Completed operation anyway: the task read as paid, the journal agreed, and the part that
        // never arrived left no trace anyone could act on. Left at Pivoted it is owed, and
        // CommerceRelayService escalates it once it has been stuck past its deadline.
        //
        // The board is still shown as claimed, because it is: the status committed before any of
        // this and re-offering the task would pay the delivered half twice.
        if (delivered)
        {
            await _journal
                .TransitionAsync(
                    operation,
                    CommerceOperationState.Completed,
                    CommerceStepKeys.REWARD_PAYOUT,
                    null,
                    ct
                )
                .ConfigureAwait(true);
        }
        else
        {
            _logger.LogError(
                "Daily task {AssignmentId} for player {PlayerId} was claimed but part of its reward "
                    + "could not be handed over; operation {OperationId} is owed.",
                assignment.Id,
                PlayerId,
                operation
            );
        }

        await Presence
            .SendComposerAsync(
                new DailyTasksTaskUpdateMessageComposer
                {
                    TaskId = assignment.Id,
                    Repeats = assignment.Repeats,
                    Status = assignment.Status,
                    SecondsLeft = SecondsLeft(assignment.ExpiresAt),
                }
            )
            .ConfigureAwait(true);

        await _events
            .PublishAsync(new DailyTaskClaimedEvent(PlayerId, assignment.Id), ct)
            .ConfigureAwait(true);
    }

    /// <summary>
    /// Grants one reward. Currency is handed over through the wallet; a reward naming a product item
    /// type is described to the client but not materialised — there is no furniture-granting path
    /// wired to daily tasks yet, and silently dropping it without a trace would be worse.
    /// </summary>
    private async Task<bool> GrantAsync(
        DailyTaskRewardEntity reward,
        CommerceOperationId operation,
        CancellationToken ct
    )
    {
        if (reward.Amount <= 0)
        {
            return true;
        }

        IPlayerWalletGrain wallet = _grainFactory.GetPlayerWalletGrain((long)PlayerId);

        if (CurrencyRewardRules.TryParseNamed(reward.RewardTypeId, out int rewardType))
        {
            // CreditOnceAsync, not GrantCurrencyAsync: the credit and the receipt that says it
            // happened commit together, under this reward's own step. The status is committed
            // before the payout runs, so everything here is owed from before it starts -- and an
            // unreceipted credit made that debt impossible to settle safely, because nothing could
            // tell a retry from a second payout. A bundle of three rewards that died on the second
            // could not be finished by hand or by anything else without risking paying the first
            // again.
            //
            // Keyed by the reward row so the rewards of one task do not share a receipt: finishing
            // the two that are owed must not be blocked by the one that already went out.
            await wallet
                .CreditOnceAsync(
                    [
                        new WalletDebitRequest
                        {
                            CurrencyKind = CurrencyRewardRules.KindFor(rewardType),
                            Amount = reward.Amount,
                        },
                    ],
                    operation,
                    $"{CommerceStepKeys.REWARD_PAYOUT}:{reward.Id}",
                    ct
                )
                .ConfigureAwait(true);

            // A false return means the receipt was already there -- this reward went out on an
            // earlier attempt. Delivered either way; what it must not mean is "not paid".
            return true;
        }

        _logger.LogWarning(
            "Daily-task reward {RewardTypeId} for player {PlayerId} is not a currency and was not granted; item rewards are not wired yet.",
            reward.RewardTypeId,
            PlayerId
        );

        // Not delivered, and said so. The caller leaves the operation owed rather than completing
        // over it -- the board promised this reward and the player did not get it.
        return false;
    }

    private async Task<List<PlayerDailyTaskEntity>> LoadAssignmentsAsync(
        VortexDbContext dbCtx,
        DateOnly today,
        CancellationToken ct
    ) =>
        await dbCtx
            .PlayerDailyTasks.Include(a => a.DailyTaskEntity)
            .Where(a => a.PlayerEntityId == PlayerId && a.AssignedOn == today)
            .OrderBy(a => a.DailyTaskEntity!.IsBonus)
            .ThenBy(a => a.DailyTaskEntity!.SortOrder)
            .ThenBy(a => a.Id)
            .ToListAsync(ct)
            .ConfigureAwait(true);

    private async Task<List<PlayerDailyTaskEntity>> DrawBoardAsync(
        VortexDbContext dbCtx,
        DateOnly today,
        int taskCount,
        int bonusCount,
        CancellationToken ct
    )
    {
        List<DailyTaskEntity> definitions = await dbCtx
            .DailyTasks.AsNoTracking()
            .Where(t => t.Enabled)
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.Id)
            .ToListAsync(ct)
            .ConfigureAwait(true);

        if (definitions.Count == 0)
        {
            return [];
        }

        List<int> ordinary = [.. definitions.Where(t => !t.IsBonus).Select(t => t.Id)];
        List<int> bonus = [.. definitions.Where(t => t.IsBonus).Select(t => t.Id)];

        List<int> drawn =
        [
            .. DailyTaskBoardDrawer.Draw(ordinary, PlayerId, today, taskCount),
            .. DailyTaskBoardDrawer.Draw(bonus, PlayerId, today, bonusCount),
        ];

        if (drawn.Count == 0)
        {
            return [];
        }

        // The board lapses at the next UTC midnight, which is the same boundary the day key uses --
        // otherwise a task drawn at 23:59 would live a full day into the next board.
        DateTime expiresAt = today.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        foreach (int definitionId in drawn)
        {
            dbCtx.PlayerDailyTasks.Add(
                new PlayerDailyTaskEntity
                {
                    PlayerEntityId = PlayerId,
                    DailyTaskEntityId = definitionId,
                    AssignedOn = today,
                    Repeats = 0,
                    Status = DailyTaskStatus.Available,
                    ExpiresAt = expiresAt,
                }
            );
        }

        await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);

        return await LoadAssignmentsAsync(dbCtx, today, ct).ConfigureAwait(true);
    }

    private async Task<ImmutableArray<DailyTaskSnapshot>> BuildSnapshotsAsync(
        VortexDbContext dbCtx,
        List<PlayerDailyTaskEntity> assignments,
        CancellationToken ct
    )
    {
        if (assignments.Count == 0)
        {
            return ImmutableArray<DailyTaskSnapshot>.Empty;
        }

        List<int> definitionIds = [.. assignments.Select(a => a.DailyTaskEntityId).Distinct()];

        // One query for every reward on the board rather than one per task.
        ILookup<int, DailyTaskRewardEntity> rewardsByTask = (
            await dbCtx
                .DailyTaskRewards.AsNoTracking()
                .Where(r => definitionIds.Contains(r.DailyTaskEntityId))
                .OrderBy(r => r.Id)
                .ToListAsync(ct)
                .ConfigureAwait(true)
        ).ToLookup(r => r.DailyTaskEntityId);

        return
        [
            .. assignments
                .Where(a => a.DailyTaskEntity is not null)
                .Select(a => new DailyTaskSnapshot
                {
                    TaskId = a.Id,
                    TaskCode = a.DailyTaskEntity!.TaskCode,
                    QuestTypeCode = a.DailyTaskEntity.QuestTypeCode,
                    IsBonus = a.DailyTaskEntity.IsBonus,
                    ImageVersion = a.DailyTaskEntity.ImageVersion,
                    CatalogName = a.DailyTaskEntity.CatalogName,
                    RequiredRepeats = Math.Max(1, a.DailyTaskEntity.RequiredRepeats),
                    Repeats = a.Repeats,
                    Status = a.Status,
                    SecondsLeft = SecondsLeft(a.ExpiresAt),
                    Rewards =
                    [
                        .. rewardsByTask[a.DailyTaskEntityId]
                            .Select(r => new DailyTaskRewardSnapshot
                            {
                                ProductItemTypeId = r.ProductItemTypeId,
                                RewardTypeId = r.RewardTypeId,
                                ExtraParams = r.ExtraParams,
                                Amount = r.Amount,
                            }),
                    ],
                }),
        ];
    }

    /// <summary>
    /// Seconds until the assignment lapses, negative once it has. The sign is the contract: the
    /// client's own <c>isExpired</c> is "secondsLeft &lt; 0 and not still available", so clamping
    /// this at zero would leave a lapsed task looking live forever.
    /// </summary>
    private static int SecondsLeft(DateTime expiresAt)
    {
        double seconds = (expiresAt - DateTime.UtcNow).TotalSeconds;

        return (int)Math.Clamp(seconds, int.MinValue, int.MaxValue);
    }
}
