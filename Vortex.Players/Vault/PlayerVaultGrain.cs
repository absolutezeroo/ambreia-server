using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orleans;
using Vortex.Database.Commerce;
using Vortex.Database.Context;
using Vortex.Database.Entities.Players;
using Vortex.Primitives.Commerce;
using Vortex.Primitives.Events;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Orleans.Snapshots.Vault;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Players.Wallet;
using Vortex.Primitives.Vault;
using Vortex.Primitives.Vault.Enums;

namespace Vortex.Players.Vault;

/// <summary>
/// A player's Earnings window. Sources describe what was earned (<see cref="IncomeEarning"/>); this
/// grain alone decides whether it waits here or lands in the wallet, and pays each row through the
/// settler registered for its reward type.
/// </summary>
internal sealed class PlayerVaultGrain(
    IDbContextFactory<VortexDbContext> dbCtxFactory,
    IGrainFactory grainFactory,
    IEventPublisher events,
    VaultRewardSettlerMap settlers,
    ILogger<PlayerVaultGrain> logger
) : Grain, IPlayerVaultGrain
{
    /// <summary>
    /// Reward type of the row that spends the key of a currency paid direct because it has no
    /// Earnings type (diamonds, other activity points). Claimed at birth, so the status packet — which
    /// reads unclaimed rows only — never sees it; deliberately outside the wire enum.
    /// </summary>
    private const int DirectOnlyRewardType = -1;

    private readonly List<PlayerVaultIncomeRewardEntity> _unclaimed = [];

    private int PlayerId => (int)this.GetPrimaryKeyLong();

    public override async Task OnActivateAsync(CancellationToken ct)
    {
        await using VortexDbContext dbCtx = await dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(true);

        _unclaimed.AddRange(
            await dbCtx
                .PlayerVaultIncomeRewards.AsNoTracking()
                .Where(r => r.PlayerEntityId == PlayerId && r.ClaimedAt == null)
                .ToListAsync(ct)
                .ConfigureAwait(true)
        );
    }

    public Task<List<IncomeRewardSnapshot>> GetIncomeRewardsAsync(CancellationToken ct) =>
        Task.FromResult(
            _unclaimed
                .Select(r => new IncomeRewardSnapshot
                {
                    RewardCategory = (VaultRewardCategoryType)r.RewardCategory,
                    RewardType = (VaultRewardType)r.RewardType,
                    Amount = r.Amount,
                    ProductCode = r.ProductCode,
                })
                .ToList()
        );

    public async Task EarnAsync(IncomeEarning earning, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(earning.SourceKey);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            earning.SourceKey.Length,
            IncomeEarning.MaxSourceKeyLength
        );

        if (earning.Amount <= 0)
        {
            return;
        }

        // One record spends a key whichever way it was paid, so a retry that lands after the
        // operator flipped the category's switch finds it spent instead of paying it the other way.
        if (await IsKeySpentAsync(earning.SourceKey, ct).ConfigureAwait(true))
        {
            return;
        }

        bool toVault = await grainFactory
            .GetServerConfigGrain()
            .GetBoolAsync(
                EarningsRoutingPolicy.KeyFor(earning.Category),
                EarningsConfig.ToVaultDefault
            )
            .ConfigureAwait(true);

        bool hasRewardType = VaultRewardRouting.TryGetRewardType(
            earning.Currency,
            out VaultRewardType rewardType
        );

        if (!toVault || !hasRewardType)
        {
            // Direct: the wallet receipt makes the credit itself idempotent, and the row inserted
            // after it, claimed at birth, spends the key for the vault path too. A crash between the
            // two — credit committed, row never lands — is covered without a retry: IsKeySpentAsync
            // also reads the direct receipt, so a later EarnAsync for the same key (vault or direct)
            // finds it already spent.
            await grainFactory
                .GetPlayerWalletGrain(PlayerId)
                .CreditOnceAsync(
                    [
                        new WalletDebitRequest
                        {
                            CurrencyKind = earning.Currency,
                            Amount = earning.Amount,
                        },
                    ],
                    CommerceOperationId.Deterministic(
                        CommerceOperationKind.VaultIncomeDirect,
                        PlayerId
                    ),
                    earning.SourceKey,
                    ct
                )
                .ConfigureAwait(true);

            await TryInsertAsync(
                    earning,
                    hasRewardType ? (int)rewardType : DirectOnlyRewardType,
                    DateTime.UtcNow,
                    ct
                )
                .ConfigureAwait(true);

            return;
        }

        if (await TryInsertAsync(earning, (int)rewardType, null, ct).ConfigureAwait(true))
        {
            await events
                .PublishAsync(new VaultIncomeDepositedEvent(PlayerId, earning.Category), ct)
                .ConfigureAwait(true);
        }
    }

    public async Task<bool> ClaimAsync(VaultRewardCategoryType category, CancellationToken ct)
    {
        List<PlayerVaultIncomeRewardEntity> candidates = _unclaimed
            .Where(r =>
                category == VaultRewardCategoryType.All
                || (VaultRewardCategoryType)r.RewardCategory == category
            )
            .ToList();

        List<PlayerVaultIncomeRewardEntity> settled = [];
        bool failed = false;

        // One credit per row, in sequence: each is its own idempotent receipt, so a crash part-way
        // leaves the rest unclaimed and the next claim finishes the job without paying twice.
        foreach (PlayerVaultIncomeRewardEntity row in candidates)
        {
            if (!settlers.TryGet((VaultRewardType)row.RewardType, out IVaultRewardSettler? settler))
            {
                continue; // no settler for this type yet (products): the row waits
            }

            try
            {
                await settler.SettleAsync(PlayerId, ToRow(row), ct).ConfigureAwait(true);
                settled.Add(row);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed = true;
                logger.LogError(
                    ex,
                    "Earnings row {RowId} ({Category}) could not be paid to player {PlayerId}; it stays unclaimed.",
                    row.Id,
                    (VaultRewardCategoryType)row.RewardCategory,
                    PlayerId
                );
            }
        }

        if (settled.Count > 0)
        {
            try
            {
                await MarkClaimedAsync(settled, ct).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Paid but not marked: the rows stay unclaimed, and the next claim marks them
                // after their receipts answer "already credited". The client still gets an answer.
                logger.LogError(
                    ex,
                    "{Count} Earnings rows were paid to player {PlayerId} but could not be marked claimed; the next claim will mark them.",
                    settled.Count,
                    PlayerId
                );
                return false;
            }

            await events
                .PublishAsync(
                    new VaultIncomeClaimedEvent(PlayerId, category.ToString(), settled.Count),
                    ct
                )
                .ConfigureAwait(true);
        }

        return settled.Count > 0 && !failed;
    }

    /// <summary>
    /// Checked against the table, not the unclaimed cache: a key whose row was already claimed, or
    /// paid direct, must stay spent. Also checked against the direct path's wallet receipt, so a
    /// crash between that credit committing and its row landing does not leave the key payable again.
    /// </summary>
    private async Task<bool> IsKeySpentAsync(string sourceKey, CancellationToken ct)
    {
        if (await HasRowAsync(sourceKey, ct).ConfigureAwait(true))
        {
            return true;
        }

        await using VortexDbContext dbCtx = await dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(true);

        return await dbCtx
            .HasCommittedReceiptAsync(
                CommerceOperationId.Deterministic(
                    CommerceOperationKind.VaultIncomeDirect,
                    PlayerId
                ),
                sourceKey,
                ct
            )
            .ConfigureAwait(true);
    }

    private async Task<bool> HasRowAsync(string sourceKey, CancellationToken ct)
    {
        await using VortexDbContext dbCtx = await dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(true);

        return await dbCtx
            .PlayerVaultIncomeRewards.AnyAsync(
                r => r.PlayerEntityId == PlayerId && r.SourceKey == sourceKey,
                ct
            )
            .ConfigureAwait(true);
    }

    /// <summary>
    /// Records the earning's key. <paramref name="claimedAt"/> is set for a direct payment, whose row
    /// only spends the key and is never shown. False when another writer recorded the key first —
    /// the unique <c>(player_id, source_key)</c> index is the backstop for a second silo racing this one.
    /// </summary>
    private async Task<bool> TryInsertAsync(
        IncomeEarning earning,
        int rewardType,
        DateTime? claimedAt,
        CancellationToken ct
    )
    {
        PlayerVaultIncomeRewardEntity entity = new()
        {
            PlayerEntityId = PlayerId,
            RewardCategory = (int)earning.Category,
            RewardType = rewardType,
            Amount = earning.Amount,
            ProductCode = string.Empty,
            SourceKey = earning.SourceKey,
            ClaimedAt = claimedAt,
        };

        await using (
            VortexDbContext dbCtx = await dbCtxFactory.CreateDbContextAsync(ct).ConfigureAwait(true)
        )
        {
            dbCtx.PlayerVaultIncomeRewards.Add(entity);

            try
            {
                await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);
            }
            catch (DbUpdateException ex)
            {
                // Only a row that now exists makes this the duplicate race. Anything else is a
                // real write failure, and swallowing it would drop an earning without a trace.
                // Row only, not the wallet receipt: on the direct path that receipt was committed
                // a moment ago by this very call, and would pass any failure off as a duplicate.
                if (await HasRowAsync(earning.SourceKey, ct).ConfigureAwait(true))
                {
                    logger.LogInformation(
                        ex,
                        "Earning {SourceKey} for player {PlayerId} was already recorded.",
                        earning.SourceKey,
                        PlayerId
                    );
                    return false;
                }

                logger.LogError(
                    ex,
                    "Earning {SourceKey} for player {PlayerId} could not be recorded.",
                    earning.SourceKey,
                    PlayerId
                );
                throw;
            }
        }

        if (claimedAt is null)
        {
            _unclaimed.Add(entity);
        }

        return true;
    }

    private async Task MarkClaimedAsync(
        List<PlayerVaultIncomeRewardEntity> rows,
        CancellationToken ct
    )
    {
        List<int> ids = rows.Select(r => r.Id).ToList();
        DateTime now = DateTime.UtcNow;

        await using VortexDbContext dbCtx = await dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(true);

        // Tracked update rather than ExecuteUpdate: the InMemory provider used by the tests does
        // not support bulk updates, and a claim touches a handful of rows.
        List<PlayerVaultIncomeRewardEntity> tracked = await dbCtx
            .PlayerVaultIncomeRewards.Where(r => ids.Contains(r.Id))
            .ToListAsync(ct)
            .ConfigureAwait(true);

        foreach (PlayerVaultIncomeRewardEntity row in tracked)
        {
            row.ClaimedAt = now;
        }

        await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);

        _unclaimed.RemoveAll(r => ids.Contains(r.Id));
    }

    private static VaultIncomeRow ToRow(PlayerVaultIncomeRewardEntity r) =>
        new(
            r.Id,
            (VaultRewardCategoryType)r.RewardCategory,
            (VaultRewardType)r.RewardType,
            r.Amount,
            r.ProductCode
        );
}
