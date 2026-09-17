using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Orleans;
using Vortex.Database.Commerce;
using Vortex.Database.Context;
using Vortex.Database.Entities.Commerce;
using Vortex.Database.Entities.Players;
using Vortex.Primitives.Commerce;
using Vortex.Primitives.Events;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players.Enums.Wallet;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Players.Providers;
using Vortex.Primitives.Players.Snapshots;
using Vortex.Primitives.Players.Wallet;

namespace Vortex.Players.Grains;

internal sealed class PlayerWalletGrain(
    IDbContextFactory<VortexDbContext> dbCtxFactory,
    ICurrencyTypeProvider currencyTypeProvider,
    IGrainFactory grainFactory,
    IEventPublisher events,
    ILogger<PlayerWalletGrain> logger
) : Grain, IPlayerWalletGrain
{
    private readonly IDbContextFactory<VortexDbContext> _dbCtxFactory = dbCtxFactory;
    private readonly ICurrencyTypeProvider _currencyTypeProvider = currencyTypeProvider;
    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly IEventPublisher _events = events;
    private readonly ILogger<PlayerWalletGrain> _logger = logger;

    /// <summary>What a debit or refund receipt records. Only its presence is read today; the value is
    /// stored so a later step that needs the earlier answer has one to read.</summary>
    private const string RECEIPT_APPLIED = "applied";

    private (string Currency, int? ActivityPointType) DescribeCurrency(CurrencyKind kind)
    {
        // Resolve the human currency name from the currency_types table; fall back to the enum name.
        string name =
            _currencyTypeProvider.GetCurrencyTypeByKind(kind)?.Name ?? kind.CurrencyType.ToString();

        int? activityPointType =
            kind.CurrencyType == CurrencyType.ActivityPoints ? kind.ActivityPointType : null;

        return (name, activityPointType);
    }

    private readonly Dictionary<CurrencyKind, WalletCurrencySnapshot> _currenciesByKind = [];

    public override async Task OnActivateAsync(CancellationToken ct)
    {
        await HydrateAsync(ct);
    }

    public Task<WalletDebitResult> TryDebitAsync(
        List<WalletDebitRequest> requests,
        CancellationToken ct
    ) => TryDebitAsync(requests, CommerceOperationId.None, ct);

    public async Task<WalletDebitResult> TryDebitAsync(
        List<WalletDebitRequest> requests,
        CommerceOperationId operationId,
        CancellationToken ct
    )
    {
        if (
            TryNormalizeRequests(requests, out List<WalletDebitRequest> normalizedRequests)
            && normalizedRequests.Count > 0
        )
        {
            // A fresh DbContext is created per attempt (rather than shared across retries) so that a
            // transient failure retried by the MySQL execution strategy never resubmits state left
            // half-applied by a rolled-back attempt.
            await using VortexDbContext strategyProbe = await _dbCtxFactory
                .CreateDbContextAsync(ct)
                .ConfigureAwait(true);
            IExecutionStrategy strategy = strategyProbe.Database.CreateExecutionStrategy();

            (
                WalletDebitFailure? failure,
                List<WalletCurrencyUpdateSnapshot> updates,
                bool replayed
            ) = await strategy
                .ExecuteAsync(async () =>
                {
                    await using VortexDbContext dbCtx = await _dbCtxFactory
                        .CreateDbContextAsync(ct)
                        .ConfigureAwait(true);
                    await using IDbContextTransaction tx = await dbCtx
                        .Database.BeginTransactionAsync(ct)
                        .ConfigureAwait(true);

                    List<WalletCurrencyUpdateSnapshot> attemptUpdates =
                        new List<WalletCurrencyUpdateSnapshot>(normalizedRequests.Count);

                    foreach (WalletDebitRequest request in normalizedRequests)
                    {
                        try
                        {
                            WalletCurrencyUpdateSnapshot update = await ProcessDebitRequestAsync(
                                dbCtx,
                                request,
                                ct
                            );

                            if (update.ChangedBy != request.Amount)
                            {
                                // Specific type (CA2201): a bare Exception cannot be caught selectively, and
                                // this is a wallet invariant breach — the amount actually debited did not
                                // match what was asked for — not an arbitrary failure.
                                throw new InvalidOperationException(
                                    $"Wallet debit changed {update.ChangedBy} but {request.Amount} was "
                                        + $"requested for {request.CurrencyKind.CurrencyType}."
                                );
                            }

                            attemptUpdates.Add(update);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(
                                ex,
                                "Wallet debit failed for player {PlayerId} ({Currency} x{Amount})",
                                this.GetPrimaryKeyLong(),
                                request.CurrencyKind.CurrencyType,
                                request.Amount
                            );

                            await tx.RollbackAsync(ct);

                            return (
                                new WalletDebitFailure
                                {
                                    CurrencyKind = request.CurrencyKind,
                                    Amount = request.Amount,
                                },
                                attemptUpdates,
                                false
                            );
                        }
                    }

                    // The receipt joins the debit's own transaction: mutation and proof commit
                    // together, or neither does. A replay loses this insert on the unique index,
                    // takes the whole attempt down with it, and is answered from the receipt
                    // that is already there.
                    if (!operationId.IsNone)
                    {
                        dbCtx.CommerceReceipts.Add(
                            new CommerceReceiptEntity
                            {
                                OperationId = operationId.Value,
                                StepKey = CommerceStepKeys.DEBIT,
                                Result = RECEIPT_APPLIED,
                                CreatedAt = DateTime.UtcNow,
                            }
                        );
                    }

                    try
                    {
                        await dbCtx.SaveChangesAsync(ct);
                        await tx.CommitAsync(ct);
                    }
                    catch (DbUpdateException ex)
                    {
                        await tx.RollbackAsync(ct);

                        // Nothing of this attempt committed. Calling it a replay is only honest if
                        // the earlier attempt's receipt is genuinely there, because the caller acts
                        // on that answer by granting the goods: read the receipt instead of reading
                        // the exception type. Without an operation there is no receipt and no
                        // replay to claim, so that case leaves here too.
                        if (
                            operationId.IsNone
                            || !await dbCtx.HasCommittedReceiptAsync(
                                operationId,
                                CommerceStepKeys.DEBIT,
                                ct
                            )
                        )
                        {
                            throw;
                        }

                        _logger.LogInformation(
                            ex,
                            "Wallet debit for player {PlayerId} replayed operation {OperationId}; "
                                + "the earlier debit stands.",
                            this.GetPrimaryKeyLong(),
                            operationId
                        );

                        return (null, attemptUpdates, true);
                    }

                    return ((WalletDebitFailure?)null, attemptUpdates, false);
                })
                .ConfigureAwait(true);

            if (replayed)
            {
                // Nothing was committed this time round, so the cached balances have to go back to
                // what the earlier debit left them at — and no currency event is published, because
                // publishing one per retry is how a quest counts a purchase twice.
                await RollbackUpdatesAsync(updates, ct);

                return WalletDebitResult.Success();
            }

            if (failure is not null)
            {
                await RollbackUpdatesAsync(updates, ct);

                return WalletDebitResult.InsufficientBalance(failure);
            }

            // The debit is committed and its receipt with it. Everything below only tells other
            // people about it, and none of it is allowed to change what this method returns.
            //
            // It used to: the caller is ExecutePurchaseAsync, which calls the debit BEFORE its
            // compensation block, so an exception thrown from here left the money taken, the grant
            // never run and no refund attempted. A failed presence call is a stale credit counter
            // until the next update; a thrown one was the player's balance.
            try
            {
                IPlayerPresenceGrain playerPresence = _grainFactory.GetPlayerPresenceGrain(
                    (int)this.GetPrimaryKeyLong()
                );

                foreach (WalletCurrencyUpdateSnapshot update in updates)
                {
                    await playerPresence.OnCurrencyUpdateAsync(update, ct);

                    (string currency, int? activityPointType) = DescribeCurrency(
                        update.CurrencyKind
                    );

                    await _events
                        .PublishAsync(
                            new CurrencyChangedEvent(
                                (int)this.GetPrimaryKeyLong(),
                                currency,
                                activityPointType,
                                -update.ChangedBy,
                                update.Amount
                            ),
                            ct
                        )
                        .ConfigureAwait(true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Wallet debit for player {PlayerId} committed, but announcing it failed. The "
                        + "debit stands; the client's balance is stale until the next update.",
                    this.GetPrimaryKeyLong()
                );
            }
        }

        return WalletDebitResult.Success();
    }

    public Task RollbackUpdatesAsync(
        List<WalletCurrencyUpdateSnapshot> updates,
        CancellationToken ct
    )
    {
        if (updates.Count == 0)
        {
            return Task.CompletedTask;
        }

        foreach (WalletCurrencyUpdateSnapshot? update in updates)
        {
            if (update is null || update.ChangedBy == 0)
            {
                continue;
            }

            if (
                _currenciesByKind.TryGetValue(
                    update.CurrencyKind,
                    out WalletCurrencySnapshot? snapshot
                )
            )
            {
                _currenciesByKind[update.CurrencyKind] = snapshot with
                {
                    Amount = snapshot.Amount + update.ChangedBy,
                };
            }
        }

        return Task.CompletedTask;
    }

    public Task<int> GetAmountForCurrencyAsync(CurrencyKind kind, CancellationToken ct) =>
        Task.FromResult(
            _currenciesByKind.TryGetValue(kind, out WalletCurrencySnapshot? snapshot)
                ? snapshot.Amount
                : 0
        );

    public Task<Dictionary<int, int>> GetActivityPointsAsync(CancellationToken ct)
    {
        Dictionary<int, int> result = new Dictionary<int, int>();

        foreach (WalletCurrencySnapshot? currency in _currenciesByKind.Values)
        {
            if (
                currency is null
                || currency.CurrencyKind.CurrencyType != CurrencyType.ActivityPoints
            )
            {
                continue;
            }

            result[currency.CurrencyKind.ActivityPointType ?? -1] = currency.Amount;
        }

        return Task.FromResult(result);
    }

    private static bool TryNormalizeRequests(
        List<WalletDebitRequest> proposed,
        out List<WalletDebitRequest> normalized
    )
    {
        normalized = [];

        Dictionary<CurrencyKind, int> totals = new Dictionary<CurrencyKind, int>(proposed.Count);

        foreach (WalletDebitRequest? request in proposed)
        {
            if (request is null || request.Amount <= 0)
            {
                continue;
            }

            int cost = request.Amount;

            if (totals.TryGetValue(request.CurrencyKind, out int total))
            {
                cost += total;
            }

            totals[request.CurrencyKind] = cost;
        }

        foreach ((CurrencyKind kind, int total) in totals)
        {
            if (total <= 0)
            {
                continue;
            }

            normalized.Add(new WalletDebitRequest { CurrencyKind = kind, Amount = total });
        }

        return true;
    }

    private async Task<WalletCurrencyUpdateSnapshot> ProcessDebitRequestAsync(
        VortexDbContext dbCtx,
        WalletDebitRequest request,
        CancellationToken ct
    )
    {
        int changedBy = 0;
        int currentAmount = 0;
        int cost = request.Amount;

        if (
            _currenciesByKind.TryGetValue(
                request.CurrencyKind,
                out WalletCurrencySnapshot? snapshot
            )
        )
        {
            int currencyId = snapshot.Id;
            int playerId = (int)this.GetPrimaryKeyLong();

            // The database decides whether the balance covers this, not the grain. It used to read
            // the row, compare in C# and subtract in memory -- correct only for as long as this
            // grain is the wallet's sole writer, which is true today, guaranteed by nothing, and
            // false for the moment a double activation overlaps during a silo handover or a network
            // partition. There is no concurrency token anywhere in the schema to catch it either.
            //
            // As a conditional UPDATE the race cannot be lost: two debits for the last hundred
            // credits both run "WHERE amount >= 100", one affects a row and one affects none.
            if (cost > 0)
            {
                int claimed = await dbCtx
                    .PlayerCurrencies.Where(x =>
                        x.Id == currencyId && x.PlayerEntityId == playerId && x.Amount >= cost
                    )
                    .ExecuteUpdateAsync(
                        u => u.SetProperty(p => p.Amount, p => p.Amount - cost),
                        ct
                    );

                if (claimed == 1)
                {
                    changedBy = cost;
                }
            }

            // Read back rather than compute: ExecuteUpdate goes round the change tracker, so the
            // balance to report is whatever the row now holds. A currency the player has no row for
            // reads as zero, which is what it was worth before too.
            currentAmount = await dbCtx
                .PlayerCurrencies.AsNoTracking()
                .Where(x => x.Id == currencyId && x.PlayerEntityId == playerId)
                .Select(x => x.Amount)
                .FirstOrDefaultAsync(ct);

            _currenciesByKind[request.CurrencyKind] = snapshot with { Amount = currentAmount };
        }

        return new()
        {
            CurrencyKind = request.CurrencyKind,
            ChangedBy = changedBy,
            Amount = currentAmount,
        };
    }

    public async Task GrantCreditsAsync(int amount, CancellationToken ct) =>
        await GrantCurrencyAsync(
                new CurrencyKind { CurrencyType = CurrencyType.Credits },
                amount,
                ct
            )
            .ConfigureAwait(true);

    public async Task GrantActivityPointsAsync(
        int activityPointType,
        int amount,
        CancellationToken ct
    ) =>
        await GrantCurrencyAsync(
                new CurrencyKind
                {
                    CurrencyType = CurrencyType.ActivityPoints,
                    ActivityPointType = activityPointType,
                },
                amount,
                ct
            )
            .ConfigureAwait(true);

    public async Task CreditBackAsync(List<WalletDebitRequest> requests, CancellationToken ct)
    {
        foreach (WalletDebitRequest request in requests)
        {
            bool granted = await GrantCurrencyAsync(request.CurrencyKind, request.Amount, ct)
                .ConfigureAwait(true);

            if (!granted)
            {
                // A refund that does not land is money the player paid and never got back, so it is
                // worth an error even though there is nothing sensible to do about it here.
                _logger.LogError(
                    "Refund of {Amount} {Currency} to player {PlayerId} did not land.",
                    request.Amount,
                    request.CurrencyKind.CurrencyType,
                    this.GetPrimaryKeyLong()
                );
            }
        }
    }

    public async Task CreditBackAsync(
        List<WalletDebitRequest> requests,
        CommerceOperationId operationId,
        CancellationToken ct
    )
    {
        if (operationId.IsNone)
        {
            await CreditBackAsync(requests, ct).ConfigureAwait(true);

            return;
        }

        await CreditOnceAsync(requests, operationId, CommerceStepKeys.REFUND, ct)
            .ConfigureAwait(true);
    }

    public async Task<bool> CreditOnceAsync(
        List<WalletDebitRequest> credits,
        CommerceOperationId operationId,
        string stepKey,
        CancellationToken ct
    )
    {
        List<WalletDebitRequest> requests = credits;

        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(true);

        // Every currency of the refund and the receipt that vouches for it go in one commit. Split
        // across two there is no safe order: receipt first loses the refund to a crash, refund first
        // pays it twice on the retry.
        List<(CurrencyKind Kind, int Amount, PlayerCurrencyEntity Entity)> credited = [];

        foreach (WalletDebitRequest request in requests)
        {
            if (request.Amount <= 0)
            {
                continue;
            }

            PlayerCurrencyEntity? entity = await ResolveCurrencyRowAsync(
                    dbCtx,
                    request.CurrencyKind,
                    ct
                )
                .ConfigureAwait(true);

            if (entity is null)
            {
                _logger.LogError(
                    "Refund of {Amount} {Currency} to player {PlayerId} did not land.",
                    request.Amount,
                    request.CurrencyKind.CurrencyType,
                    this.GetPrimaryKeyLong()
                );

                continue;
            }

            entity.Amount += request.Amount;
            credited.Add((request.CurrencyKind, request.Amount, entity));
        }

        dbCtx.CommerceReceipts.Add(
            new CommerceReceiptEntity
            {
                OperationId = operationId.Value,
                StepKey = stepKey,
                Result = RECEIPT_APPLIED,
                CreatedAt = DateTime.UtcNow,
            }
        );

        try
        {
            await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);
        }
        catch (DbUpdateException ex)
        {
            // Same rule as the debit above: the receipt decides, not the exception type. Returning
            // false here tells the caller the credit is already the player's, and a caller told
            // that about a write that never landed closes a debt nobody ever paid.
            if (!await dbCtx.HasCommittedReceiptAsync(operationId, stepKey, ct))
            {
                throw;
            }

            _logger.LogInformation(
                ex,
                "Step {StepKey} of operation {OperationId} was already credited to player "
                    + "{PlayerId}; not crediting again.",
                stepKey,
                operationId,
                this.GetPrimaryKeyLong()
            );

            return false;
        }

        foreach ((CurrencyKind kind, int amount, PlayerCurrencyEntity entity) in credited)
        {
            await AnnounceCurrencyAsync(kind, amount, entity, ct).ConfigureAwait(true);
        }

        return true;
    }

    /// <summary>
    /// The player row for a currency, the cached one where there is one and a lazily created one
    /// otherwise. The same resolution <see cref="GrantCurrencyAsync"/> performs, without its commit,
    /// so a caller can put several credits and a receipt into one transaction.
    /// </summary>
    private async Task<PlayerCurrencyEntity?> ResolveCurrencyRowAsync(
        VortexDbContext dbCtx,
        CurrencyKind kind,
        CancellationToken ct
    )
    {
        if (_currenciesByKind.TryGetValue(kind, out WalletCurrencySnapshot? snapshot))
        {
            return await dbCtx
                .PlayerCurrencies.Where(x =>
                    x.Id == snapshot.Id && x.PlayerEntityId == (int)this.GetPrimaryKeyLong()
                )
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(true);
        }

        CurrencyTypeSnapshot? currencyType = _currencyTypeProvider.GetCurrencyTypeByKind(kind);

        if (currencyType is null || !currencyType.Enabled)
        {
            return null;
        }

        PlayerCurrencyEntity entity = new()
        {
            PlayerEntityId = (int)this.GetPrimaryKeyLong(),
            CurrencyTypeEntityId = currencyType.Id,
            Amount = currencyType.StartingAmount,
        };

        dbCtx.PlayerCurrencies.Add(entity);

        return entity;
    }

    /// <summary>Cache, event and client composer for a credit that has already committed.</summary>
    private async Task AnnounceCurrencyAsync(
        CurrencyKind kind,
        int amount,
        PlayerCurrencyEntity entity,
        CancellationToken ct
    )
    {
        _currenciesByKind[kind] = new WalletCurrencySnapshot
        {
            Id = entity.Id,
            CurrencyKind = kind,
            Amount = entity.Amount,
        };

        (string currencyName, int? activityPointType) = DescribeCurrency(kind);

        await _events
            .PublishAsync(
                new CurrencyChangedEvent(
                    (int)this.GetPrimaryKeyLong(),
                    currencyName,
                    activityPointType,
                    amount,
                    entity.Amount
                ),
                ct
            )
            .ConfigureAwait(true);

        await _grainFactory
            .GetPlayerPresenceGrain((int)this.GetPrimaryKeyLong())
            .OnCurrencyUpdateAsync(
                new WalletCurrencyUpdateSnapshot
                {
                    CurrencyKind = kind,
                    ChangedBy = amount,
                    Amount = entity.Amount,
                },
                ct
            )
            .ConfigureAwait(true);
    }

    public async Task<bool> GrantCurrencyAsync(CurrencyKind kind, int amount, CancellationToken ct)
    {
        if (amount <= 0)
        {
            return false;
        }

        await using VortexDbContext dbCtx = await _dbCtxFactory.CreateDbContextAsync(ct);

        PlayerCurrencyEntity? entity;

        if (_currenciesByKind.TryGetValue(kind, out WalletCurrencySnapshot? snapshot))
        {
            entity = await dbCtx
                .PlayerCurrencies.Where(x =>
                    x.Id == snapshot.Id && x.PlayerEntityId == (int)this.GetPrimaryKeyLong()
                )
                .FirstOrDefaultAsync(ct);

            if (entity is null)
            {
                _logger.LogWarning(
                    "Wallet grant of {Amount} {Currency} for player {PlayerId} did not land: the "
                        + "cached currency row no longer exists in the database.",
                    amount,
                    kind.CurrencyType,
                    this.GetPrimaryKeyLong()
                );

                return false;
            }
        }
        else
        {
            // Lazily create the row the same way HydrateAsync() bootstraps Credits below - a
            // player can be granted a currency (e.g. a specific activity point type) for the
            // first time via a quest/achievement reward without ever having debited or been
            // granted it before, so there is no cached row yet to update.
            CurrencyTypeSnapshot? currencyType = _currencyTypeProvider.GetCurrencyTypeByKind(kind);

            if (currencyType is null || !currencyType.Enabled)
            {
                // The most likely reason a grant does nothing: there is no currency_types row for
                // this currency at all. Nothing above here could tell that apart from success
                // before, so an operator granting emeralds saw a green result and no emeralds.
                _logger.LogWarning(
                    "Wallet grant of {Amount} {Currency} for player {PlayerId} did not land: that "
                        + "currency has no enabled currency_types row.",
                    amount,
                    kind.CurrencyType,
                    this.GetPrimaryKeyLong()
                );

                return false;
            }

            entity = new PlayerCurrencyEntity
            {
                PlayerEntityId = (int)this.GetPrimaryKeyLong(),
                CurrencyTypeEntityId = currencyType.Id,
                Amount = currencyType.StartingAmount,
            };

            dbCtx.PlayerCurrencies.Add(entity);
        }

        entity.Amount += amount;
        await dbCtx.SaveChangesAsync(ct);

        _currenciesByKind[kind] = new WalletCurrencySnapshot
        {
            Id = entity.Id,
            CurrencyKind = kind,
            Amount = entity.Amount,
        };

        (string currencyName, int? activityPointType) = DescribeCurrency(kind);

        await _events
            .PublishAsync(
                new CurrencyChangedEvent(
                    (int)this.GetPrimaryKeyLong(),
                    currencyName,
                    activityPointType,
                    amount,
                    entity.Amount
                ),
                ct
            )
            .ConfigureAwait(true);

        IPlayerPresenceGrain playerPresence = _grainFactory.GetPlayerPresenceGrain(
            (int)this.GetPrimaryKeyLong()
        );
        await playerPresence.OnCurrencyUpdateAsync(
            new WalletCurrencyUpdateSnapshot
            {
                CurrencyKind = kind,
                ChangedBy = amount,
                Amount = entity.Amount,
            },
            ct
        );

        return true;
    }

    private async Task HydrateAsync(CancellationToken ct)
    {
        _currenciesByKind.Clear();

        await using VortexDbContext dbCtx = await _dbCtxFactory.CreateDbContextAsync(ct);

        List<PlayerCurrencyEntity> entities = await dbCtx
            .PlayerCurrencies.AsNoTracking()
            .Where(x => x.PlayerEntityId == (int)this.GetPrimaryKeyLong())
            .ToListAsync(ct);

        foreach (PlayerCurrencyEntity entity in entities)
        {
            CurrencyTypeSnapshot? currencyType = _currencyTypeProvider.GetCurrencyType(
                entity.CurrencyTypeEntityId
            );

            if (currencyType is null || !currencyType.Enabled)
            {
                continue;
            }

            WalletCurrencySnapshot snapshot = new WalletCurrencySnapshot
            {
                Id = entity.Id,
                CurrencyKind = new CurrencyKind
                {
                    CurrencyType = currencyType.CurrencyType,
                    ActivityPointType = currencyType.ActivityPointType,
                },
                Amount = entity.Amount,
            };

            _currenciesByKind[snapshot.CurrencyKind] = snapshot;
        }

        CurrencyKind creditsKind = new CurrencyKind { CurrencyType = CurrencyType.Credits };

        if (!_currenciesByKind.ContainsKey(creditsKind))
        {
            CurrencyTypeSnapshot? creditType = _currencyTypeProvider.GetCurrencyTypeByKind(
                creditsKind
            );

            if (creditType is not null)
            {
                await using VortexDbContext writeCtx = await _dbCtxFactory.CreateDbContextAsync(ct);

                PlayerCurrencyEntity entity = new PlayerCurrencyEntity
                {
                    PlayerEntityId = (int)this.GetPrimaryKeyLong(),
                    CurrencyTypeEntityId = creditType.Id,
                    Amount = creditType.StartingAmount,
                };

                writeCtx.PlayerCurrencies.Add(entity);

                await writeCtx.SaveChangesAsync(ct).ConfigureAwait(true);

                _currenciesByKind[creditsKind] = new WalletCurrencySnapshot
                {
                    Id = entity.Id,
                    CurrencyKind = creditsKind,
                    Amount = entity.Amount,
                };
            }
        }
    }
}
