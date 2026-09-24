using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orleans;
using Vortex.Collectibles.Configuration;
using Vortex.Collectibles.Vault;
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

namespace Vortex.Collectibles.Grains;

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

        bool toVault = await grainFactory
            .GetServerConfigGrain()
            .GetBoolAsync(
                EarningsRoutingPolicy.KeyFor(earning.Category),
                EarningsConfig.ToVaultDefault
            )
            .ConfigureAwait(true);

        if (
            !toVault
            || !VaultRewardRouting.TryGetRewardType(
                earning.Currency,
                out VaultRewardType rewardType
            )
        )
        {
            // Direct: same idempotency, carried by the wallet receipt instead of a row.
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

            return;
        }

        if (await TryInsertAsync(earning, rewardType, ct).ConfigureAwait(true))
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
            await MarkClaimedAsync(settled, ct).ConfigureAwait(true);

            await events
                .PublishAsync(
                    new VaultIncomeClaimedEvent(PlayerId, category.ToString(), settled.Count),
                    ct
                )
                .ConfigureAwait(true);
        }

        return settled.Count > 0 && !failed;
    }

    private async Task<bool> TryInsertAsync(
        IncomeEarning earning,
        VaultRewardType rewardType,
        CancellationToken ct
    )
    {
        await using VortexDbContext dbCtx = await dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(true);

        // Checked against the table, not the unclaimed cache: a key whose row was already claimed
        // must stay spent. The unique index is the backstop for a second silo racing this one.
        bool exists = await dbCtx
            .PlayerVaultIncomeRewards.AnyAsync(
                r => r.PlayerEntityId == PlayerId && r.SourceKey == earning.SourceKey,
                ct
            )
            .ConfigureAwait(true);

        if (exists)
        {
            return false;
        }

        PlayerVaultIncomeRewardEntity entity = new()
        {
            PlayerEntityId = PlayerId,
            RewardCategory = (int)earning.Category,
            RewardType = (int)rewardType,
            Amount = earning.Amount,
            ProductCode = string.Empty,
            SourceKey = earning.SourceKey,
        };

        dbCtx.PlayerVaultIncomeRewards.Add(entity);

        try
        {
            await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);
        }
        catch (DbUpdateException ex)
        {
            logger.LogInformation(
                ex,
                "Earning {SourceKey} for player {PlayerId} was already recorded.",
                earning.SourceKey,
                PlayerId
            );
            return false;
        }

        _unclaimed.Add(entity);
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
