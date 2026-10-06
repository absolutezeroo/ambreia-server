using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orleans;
using Vortex.Database.Context;
using Vortex.Database.Entities.Collectibles;
using Vortex.Primitives.Collectibles;
using Vortex.Primitives.Collectibles.Grains;
using Vortex.Primitives.Commerce;
using Vortex.Primitives.Events;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Inventory.Grains;
using Vortex.Primitives.Inventory.Snapshots;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Enums.Wallet;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Players.Wallet;
using Vortex.Primitives.Rooms.Object;

namespace Vortex.Collectibles.Grains;

/// <summary>
/// One player's stamps and Relics.
/// </summary>
/// <remarks>
/// <para>
/// Converting is destructive and paid for, so both halves happen here rather than in a handler: the
/// grain is single-threaded per player, which is what stops a double click on the confirm dialog
/// from consuming two pieces of furniture — and the client does not disable that button until the
/// answer arrives.
/// </para>
/// <para>
/// The furniture row is soft-deleted with the same query that checks it is still the player's, so
/// even a call that somehow arrives twice can only take effect once: the second finds nothing to
/// delete and stops before anything is spent.
/// </para>
/// </remarks>
internal sealed class PlayerMintGrain(
    IDbContextFactory<VortexDbContext> dbCtxFactory,
    IGrainFactory grainFactory,
    IEventPublisher events,
    ILogger<PlayerMintGrain> logger,
    ICommerceJournal journal
) : Grain, IPlayerMintGrain
{
    private readonly ICommerceJournal _journal = journal;
    private readonly IDbContextFactory<VortexDbContext> _dbCtxFactory = dbCtxFactory;
    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly IEventPublisher _events = events;
    private readonly ILogger<PlayerMintGrain> _logger = logger;

    private PlayerId PlayerId => new((int)this.GetPrimaryKeyLong());

    public async Task<int> GetTokenBalanceAsync(CancellationToken ct)
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(true);

        return await ReadBalanceAsync(dbCtx, ct).ConfigureAwait(true);
    }

    public async Task<MintTokenPurchaseResult> PurchaseTokensAsync(
        int offerId,
        CancellationToken ct
    )
    {
        MintTokenOfferSnapshot? offer = await _grainFactory
            .GetNftMintingGrain()
            .FindTokenOfferAsync(offerId, ct)
            .ConfigureAwait(true);

        if (offer is null || offer.AmountTokens <= 0)
        {
            _logger.LogWarning(
                "Player {PlayerId} tried to buy stamp bundle {OfferId}, which is not on sale.",
                PlayerId,
                offerId
            );

            return new MintTokenPurchaseResult
            {
                Purchased = false,
                Balance = await GetTokenBalanceAsync(ct).ConfigureAwait(true),
            };
        }

        IPlayerWalletGrain wallet = _grainFactory.GetPlayerWalletGrain(PlayerId);

        CommerceOperationId operation = CommerceOperationId.New();

        await _journal
            .OpenAsync(
                operation,
                CommerceOperationKind.MintTokenPurchase,
                (int)PlayerId,
                $"stamps offer={offerId} amount={offer.AmountTokens} silver={offer.SilverPrice}",
                ct
            )
            .ConfigureAwait(true);

        // The price is the one this hotel published, never the one the client sent: the purchase
        // message carries only an offer id, and that is deliberate.
        //
        // Through the shared executor rather than a debit and a hand-written catch. The catch it
        // replaces was correct in shape and wrong in one detail: it compensated on `ct`, and a
        // cancelled request -- a client disconnecting mid-purchase, which is the commonest reason
        // the grant does not finish -- is exactly the case where that token is already cancelled and
        // the refund would never run. The executor compensates on no token for that reason, and the
        // comment saying so has been there all along.
        try
        {
            WalletPurchaseResult<int> result = await wallet
                .ExecutePurchaseAsync(
                    [SilverCost(offer.SilverPrice)],
                    operation,
                    async innerCt =>
                    {
                        await _journal
                            .TransitionAsync(
                                operation,
                                CommerceOperationState.Debited,
                                CommerceStepKeys.DEBIT,
                                null,
                                innerCt
                            )
                            .ConfigureAwait(true);

                        int credited = await AddTokensAsync(offer.AmountTokens, innerCt)
                            .ConfigureAwait(true);

                        await _journal
                            .TransitionAsync(
                                operation,
                                CommerceOperationState.Completed,
                                CommerceStepKeys.MINT_TOKENS,
                                null,
                                innerCt
                            )
                            .ConfigureAwait(true);

                        return credited;
                    },
                    _logger,
                    ct,
                    _journal
                )
                .ConfigureAwait(true);

            if (!result.Succeeded)
            {
                await _journal
                    .TransitionAsync(
                        operation,
                        CommerceOperationState.FailedBeforePivot,
                        CommerceStepKeys.DEBIT,
                        "insufficient balance",
                        ct
                    )
                    .ConfigureAwait(true);

                return new MintTokenPurchaseResult
                {
                    Purchased = false,
                    Balance = await GetTokenBalanceAsync(ct).ConfigureAwait(true),
                };
            }

            _logger.LogInformation(
                "Player {PlayerId} bought {Amount} stamp(s) for {Price} silver; balance is now {Balance}.",
                PlayerId,
                offer.AmountTokens,
                offer.SilverPrice,
                result.Reward
            );

            await _events
                .PublishAsync(
                    new MintTokensPurchasedEvent(PlayerId, offer.AmountTokens, offer.SilverPrice),
                    ct
                )
                .ConfigureAwait(true);

            return new MintTokenPurchaseResult { Purchased = true, Balance = result.Reward };
        }
        catch (Exception ex)
        {
            // The executor has already refunded and recorded it; this only keeps the caller's
            // contract, which is a result rather than a fault.
            _logger.LogError(
                ex,
                "Crediting {Amount} stamp(s) to player {PlayerId} failed after the debit; the silver was refunded.",
                offer.AmountTokens,
                PlayerId
            );

            return new MintTokenPurchaseResult
            {
                Purchased = false,
                Balance = await GetTokenBalanceAsync(ct).ConfigureAwait(true),
            };
        }
    }

    public async Task<MintOutcome> MintAsync(int itemId, CancellationToken ct)
    {
        INftMintingGrain minting = _grainFactory.GetNftMintingGrain();

        if (!await minting.IsMintingEnabledAsync(ct).ConfigureAwait(true))
        {
            return MintOutcome.MintingDisabled;
        }

        IInventoryGrain inventory = _grainFactory.GetInventoryGrain(PlayerId);
        RoomObjectId objectId = new(itemId);

        // The inventory only holds furniture that is not standing in a room, so finding the item
        // here is also what establishes that it is loose and the player's to convert.
        FurnitureItemSnapshot? item = await inventory
            .GetItemSnapshotAsync(objectId, ct)
            .ConfigureAwait(true);

        if (item is null)
        {
            return MintOutcome.NotOwned;
        }

        MintableTypeTerms? terms = await minting
            .FindMintableTermsAsync(item.Definition.Name, ct)
            .ConfigureAwait(true);

        if (terms is null)
        {
            return MintOutcome.NotMintable;
        }

        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(true);

        int balance = await ReadBalanceAsync(dbCtx, ct).ConfigureAwait(true);

        if (balance < terms.StampPrice)
        {
            return MintOutcome.NotEnoughStamps;
        }

        // Counted against the Relics that exist rather than a counter column, so deleting a mintable
        // type and recreating it cannot mint the same edition twice. Two players racing for the last
        // copy both read the same total here; the unique index on (product_code, serial_number) is
        // what actually settles it, and the loser's conversion is undone below.
        //
        // NO owner condition and NO `WalletAddress == null`, deliberately: this counts how many of
        // the classname EXIST, not who holds them. Adding the wallet filter for consistency with the
        // ownership queries would let a player transfer a Relic out to free a slot and mint another
        // one, and "#3 of 3" would stop meaning anything.
        int minted = await dbCtx
            .NftAssets.AsNoTracking()
            .CountAsync(asset => asset.ProductCode == terms.ProductCode, ct)
            .ConfigureAwait(true);

        if (terms.EditionSize > 0 && minted >= terms.EditionSize)
        {
            return MintOutcome.EditionExhausted;
        }

        // Deleting the furniture row is the step that decides the whole conversion: it is scoped to
        // this player and to a row that is not already gone, so a repeat of this call deletes
        // nothing and spends nothing.
        // No room condition here, deliberately. A room detaches its furniture from the database in
        // a deferred batch, so a row keeps naming the room it just left for as long as that flush
        // takes -- and requiring room_id to be null refused every item that had been in a room
        // moments earlier, silently. The inventory snapshot read above stands in for it: the
        // inventory only ever holds furniture that is not standing in a room.
        //
        // The chest condition is not in that position and is enforced here (NFT-MINT-029). An item
        // staked in a wired chest keeps player_id and deleted_at exactly as a loose one does, so
        // those two predicates alone would convert somebody's shop stock into a Relic on the
        // strength of a cache entry; it is the one predicate the inventory loader has that this
        // claim was missing.
        int deleted = await dbCtx
            .Furnitures.Where(furni =>
                furni.Id == itemId
                && furni.PlayerEntityId == PlayerId.Value
                && furni.WiredChestEntityId == null
                && furni.DeletedAt == null
            )
            .ExecuteUpdateAsync(
                row =>
                    row.SetProperty(furni => furni.DeletedAt, DateTime.UtcNow)
                        .SetProperty(furni => furni.RoomEntityId, (int?)null),
                ct
            )
            .ConfigureAwait(true);

        if (deleted == 0)
        {
            return MintOutcome.NotOwned;
        }

        // Carried out of the try so the record raised further down can name the Relic that was
        // actually written; the entity itself is scoped to the block that saves it.
        int mintedAssetId;
        int mintedSerialNumber;

        try
        {
            NftAssetEntity asset = new()
            {
                PlayerEntityId = PlayerId.Value,
                ProductCode = item.Definition.Name,
                FurnitureDefinitionEntityId = item.Definition.Id,
                SourceItemId = itemId,
                StampCost = terms.StampPrice,
                SerialNumber = minted + 1,
                // Copied, not looked up: lowering a cap later must not make an existing Relic read
                // as "#7 of 5".
                EditionSize = terms.EditionSize,
            };

            dbCtx.NftAssets.Add(asset);

            // The first line of this Relic's history, and the only one with no previous owner.
            dbCtx.NftAssetLedger.Add(
                new NftAssetLedgerEntity
                {
                    // The asset has no id until the save; the navigation is what tells EF to fill
                    // the key in once it does.
                    NftAssetEntity = asset,
                    NftAssetEntityId = 0,
                    FromPlayerEntityId = null,
                    ToPlayerEntityId = PlayerId.Value,
                    Reason = NftAssetLedgerReason.Minted,
                }
            );

            await SpendTokensAsync(dbCtx, terms.StampPrice, ct).ConfigureAwait(true);

            mintedAssetId = asset.Id;
            mintedSerialNumber = asset.SerialNumber;
        }
        catch (Exception ex)
        {
            // The furniture is already deleted, and there is no putting that exact item back — so
            // an equivalent one is granted rather than leaving the player with neither.
            _logger.LogError(
                ex,
                "Recording the Relic for player {PlayerId} failed after {ProductCode} was consumed; granting the furniture back.",
                PlayerId,
                item.Definition.Name
            );

            await inventory
                .GrantFurnitureDefinitionAsync(item.Definition.Id, null, ct)
                .ConfigureAwait(true);

            return MintOutcome.Failed;
        }

        // Only now does the client hear about it: the item leaves the inventory view, which is also
        // what makes the minting tab recount how many of that type are left.
        await inventory.RemoveFurnitureAsync(objectId, ct).ConfigureAwait(true);

        _logger.LogInformation(
            "Player {PlayerId} converted {ProductCode} (item {ItemId}) into a Relic for {Price} stamp(s).",
            PlayerId,
            item.Definition.Name,
            itemId,
            terms.StampPrice
        );

        await _events
            .PublishAsync(
                new RelicMintedEvent(
                    PlayerId,
                    mintedAssetId,
                    item.Definition.Id,
                    mintedSerialNumber,
                    terms.StampPrice
                ),
                ct
            )
            .ConfigureAwait(true);

        return MintOutcome.Minted;
    }

    public async Task<ImmutableArray<CollectibleAssetSnapshot>> GetAssetsAsync(CancellationToken ct)
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(true);

        // `WalletAddress == null` is what keeps one Relic from existing in two places. This answer is
        // not only the inventory tab: RoomTradingSystem.AddTradeAssetsAsync validates a trade offer
        // against it, so a Relic that leaked in here would be one a player could trade away while it
        // is sitting in somebody's wallet.
        NftAssetEntity[] assets = await dbCtx
            .NftAssets.AsNoTracking()
            .Where(asset =>
                asset.PlayerEntityId == PlayerId.Value
                && asset.DeletedAt == null
                && asset.WalletAddress == null
            )
            .OrderBy(asset => asset.Id)
            .ToArrayAsync(ct)
            .ConfigureAwait(true);

        if (assets.Length == 0)
        {
            return [];
        }

        string[] codes = [.. assets.Select(asset => asset.ProductCode).Distinct()];

        Dictionary<string, (int SpriteId, ProductType Type)> definitions =
            await FurnitureDefinitionLookup
                .ResolveByClassNameAsync(
                    dbCtx,
                    codes,
                    definition => (definition.SpriteId, definition.ProductType),
                    ct,
                    _logger
                )
                .ConfigureAwait(true);

        // What a Relic is worth is a property of the collection it belongs to, so it is read from
        // there rather than copied onto the asset: an admin re-pricing a collection item re-prices
        // every Relic of it, which is the behaviour the collections tab already has.
        Dictionary<string, int> scores = await dbCtx
            .NftCollectionItems.AsNoTracking()
            .Where(collectionItem =>
                codes.Contains(collectionItem.ProductCode) && collectionItem.DeletedAt == null
            )
            .GroupBy(collectionItem => collectionItem.ProductCode)
            .Select(group => new { ProductCode = group.Key, Score = group.Max(x => x.Score) })
            .ToDictionaryAsync(
                row => row.ProductCode,
                row => row.Score,
                StringComparer.OrdinalIgnoreCase,
                ct
            )
            .ConfigureAwait(true);

        return
        [
            .. assets.Select(asset =>
            {
                definitions.TryGetValue(
                    asset.ProductCode,
                    out (int SpriteId, ProductType Type) definition
                );

                scores.TryGetValue(asset.ProductCode, out int score);

                return new CollectibleAssetSnapshot
                {
                    AssetId = asset.Id,
                    Product = new CollectibleProductItemSnapshot
                    {
                        ProductTypeId = CollectibleProductIdentity.ForFurniture(definition.Type),
                        ItemTypeId = CollectibleProductIdentity.ItemTypeId(definition.SpriteId),
                        Score = score,
                        ProductCode = asset.ProductCode,
                    },
                };
            }),
        ];
    }

    public async Task<ImmutableArray<string>> GetLinkedWalletsAsync(CancellationToken ct)
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(true);

        return
        [
            .. await dbCtx
                .PlayerWallets.AsNoTracking()
                .Where(row => row.PlayerEntityId == PlayerId.Value)
                .OrderBy(row => row.Id)
                .Select(row => row.Address)
                .ToArrayAsync(ct)
                .ConfigureAwait(true),
        ];
    }

    // The chain is gone: Relics can no longer be transferred out.
    public Task<NftTransferResult> TransferAssetsAsync(string wallet, CancellationToken ct) =>
        Task.FromResult(Refused(NftTransferOutcome.ChainUnavailable));

    private static NftTransferResult Refused(NftTransferOutcome outcome) =>
        new() { Outcome = outcome, Transferred = 0 };

    private static WalletDebitRequest SilverCost(int amount) =>
        new()
        {
            CurrencyKind = new CurrencyKind { CurrencyType = CurrencyType.Silver },
            Amount = amount,
        };

    private async Task<int> ReadBalanceAsync(VortexDbContext dbCtx, CancellationToken ct) =>
        await dbCtx
            .PlayerMintTokens.AsNoTracking()
            .Where(row => row.PlayerEntityId == PlayerId.Value && row.DeletedAt == null)
            .Select(row => row.Balance)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(true);

    private async Task<int> AddTokensAsync(int amount, CancellationToken ct)
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(true);

        PlayerMintTokensEntity? row = await dbCtx
            .PlayerMintTokens.FirstOrDefaultAsync(
                candidate =>
                    candidate.PlayerEntityId == PlayerId.Value && candidate.DeletedAt == null,
                ct
            )
            .ConfigureAwait(true);

        if (row is null)
        {
            row = new PlayerMintTokensEntity { PlayerEntityId = PlayerId.Value, Balance = amount };

            dbCtx.PlayerMintTokens.Add(row);
        }
        else
        {
            row.Balance += amount;
        }

        await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);

        return row.Balance;
    }

    /// <summary>
    /// Takes stamps off the balance in the same save as the Relic that was paid for, so the two can
    /// never end up disagreeing.
    /// </summary>
    private async Task SpendTokensAsync(VortexDbContext dbCtx, int amount, CancellationToken ct)
    {
        PlayerMintTokensEntity? row = await dbCtx
            .PlayerMintTokens.FirstOrDefaultAsync(
                candidate =>
                    candidate.PlayerEntityId == PlayerId.Value && candidate.DeletedAt == null,
                ct
            )
            .ConfigureAwait(true);

        if (row is not null)
        {
            row.Balance -= amount;
        }

        await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);
    }
}
