using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orleans;
using Vortex.Database.Context;
using Vortex.Database.Entities.Collectibles;
using Vortex.Primitives.Collectibles;
using Vortex.Primitives.Collectibles.Grains;
using Vortex.Primitives.Events;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Inventory.Grains;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Enums.Wallet;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Players.Wallet;

namespace Vortex.Collectibles.Grains;

/// <summary>
/// The Collectors Guild shop.
/// </summary>
/// <remarks>
/// The offers are cached the way collections are — an admin edits them, players do not. The sale
/// itself is deliberately <em>not</em> cached-and-forgotten: it runs inside this single-threaded
/// grain so the last copy of a limited edition can only be sold once, however many players click at
/// the same moment.
/// </remarks>
[KeepAlive]
internal sealed class NftStoreGrain(
    IDbContextFactory<VortexDbContext> dbCtxFactory,
    IGrainFactory grainFactory,
    IEventPublisher events,
    ILogger<NftStoreGrain> logger
) : Grain, INftStoreGrain
{
    private readonly IDbContextFactory<VortexDbContext> _dbCtxFactory = dbCtxFactory;
    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly IEventPublisher _events = events;
    private readonly ILogger<NftStoreGrain> _logger = logger;

    private ImmutableArray<CachedOffer> _offers = [];
    private bool _loaded;

    public override async Task OnActivateAsync(CancellationToken ct)
    {
        await LoadAsync(ct).ConfigureAwait(true);
        await base.OnActivateAsync(ct).ConfigureAwait(true);
    }

    public async Task<ImmutableArray<NftStoreOfferSnapshot>> GetOffersAsync(CancellationToken ct)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(true);

        // Sold out is shown, not hidden: the client draws a full counter and a dead buy button for
        // it, which is the answer a player who missed a limited edition should get. Filtering on
        // IsOnSale here made that branch unreachable and the item vanish instead.
        return [.. _offers.Where(offer => offer.Enabled).Select(ToSnapshot)];
    }

    public async Task<NftStorePurchaseOutcome> PurchaseAsync(
        PlayerId playerId,
        string productCode,
        CancellationToken ct
    )
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(true);

        CachedOffer? offer = _offers.FirstOrDefault(candidate =>
            string.Equals(candidate.ProductCode, productCode, StringComparison.OrdinalIgnoreCase)
        );

        if (offer is null || !IsOnSale(offer))
        {
            // Sold out and never-existed are the same answer to the client, but not in the log: one
            // is a race between two buyers, the other is a client asking for something we never
            // offered.
            return offer is null
                ? NftStorePurchaseOutcome.UnknownOffer
                : NftStorePurchaseOutcome.SoldOut;
        }

        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(true);

        // Ordered on purpose: a classname can name several definitions (see
        // FurnitureDefinitionLookup), and an unordered First would let the database decide which
        // piece of furniture the player is handed. Same lowest-id rule as the shop listing, so the
        // item bought is the item that was drawn.
        int definitionId = await dbCtx
            .FurnitureDefinitions.AsNoTracking()
            .Where(definition =>
                definition.Name == offer.ProductCode && definition.DeletedAt == null
            )
            .OrderBy(definition => definition.Id)
            .Select(definition => definition.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(true);

        if (definitionId <= 0)
        {
            // An offer naming furniture that does not exist would take the emeralds and hand over
            // nothing, so it is refused before any of that happens.
            _logger.LogError(
                "Shop offer {ProductCode} names no furniture definition; refusing the sale.",
                offer.ProductCode
            );

            return NftStorePurchaseOutcome.NoSuchFurniture;
        }

        IPlayerWalletGrain wallet = _grainFactory.GetPlayerWalletGrain(playerId);

        WalletDebitResult debit = await wallet
            .TryDebitAsync(
                [
                    new WalletDebitRequest
                    {
                        CurrencyKind = new CurrencyKind { CurrencyType = CurrencyType.Emeralds },
                        Amount = offer.EmeraldPrice,
                    },
                ],
                ct
            )
            .ConfigureAwait(true);

        if (!debit.Succeeded)
        {
            return NftStorePurchaseOutcome.NotEnoughEmeralds;
        }

        try
        {
            await _grainFactory
                .GetInventoryGrain(playerId)
                .GrantFurnitureDefinitionAsync(definitionId, null, ct)
                .ConfigureAwait(true);

            await dbCtx
                .NftStoreOffers.Where(row => row.Id == offer.Id)
                .ExecuteUpdateAsync(
                    row => row.SetProperty(x => x.SoldCount, x => x.SoldCount + 1),
                    ct
                )
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // The emeralds are already gone at this point, so they go back before anything else.
            _logger.LogError(
                ex,
                "Shop purchase of {ProductCode} by player {PlayerId} failed after the debit; refunding.",
                offer.ProductCode,
                playerId
            );

            await wallet
                .CreditBackAsync(
                    [
                        new WalletDebitRequest
                        {
                            CurrencyKind = new CurrencyKind
                            {
                                CurrencyType = CurrencyType.Emeralds,
                            },
                            Amount = offer.EmeraldPrice,
                        },
                    ],
                    ct
                )
                .ConfigureAwait(true);

            return NftStorePurchaseOutcome.Failed;
        }

        // Counted in memory too, so the next buyer in this same activation sees the new total
        // without a database round trip.
        _offers = [.. _offers.Select(row => row.Id == offer.Id ? row.Sold() : row)];

        _logger.LogInformation(
            "Player {PlayerId} bought {ProductCode} from the collectibles shop for {Price} emeralds.",
            playerId,
            offer.ProductCode,
            offer.EmeraldPrice
        );

        await _events
            .PublishAsync(
                new NftStorePurchasedEvent(playerId, offer.ProductCode, offer.EmeraldPrice),
                ct
            )
            .ConfigureAwait(true);

        return NftStorePurchaseOutcome.Sold;
    }

    public Task ReloadAsync(CancellationToken ct) => LoadAsync(ct);

    /// <summary>
    /// Whether an offer is still buyable: enabled, and not at its limit. A limit of zero is no
    /// limit, which is the only reading that makes sense for an offer that is not a limited edition.
    /// </summary>
    private static bool IsOnSale(CachedOffer offer) =>
        offer.Enabled && (offer.MintLimit <= 0 || offer.SoldCount < offer.MintLimit);

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

            NftStoreOfferEntity[] offers = await dbCtx
                .NftStoreOffers.AsNoTracking()
                .Where(offer => offer.DeletedAt == null)
                .OrderBy(offer => offer.SortOrder)
                .ThenBy(offer => offer.Id)
                .ToArrayAsync(ct)
                .ConfigureAwait(true);

            // The client draws a shop item by looking its sprite id up in its own furniture tables,
            // so both the id and which table to use come from the definition rather than from
            // anything an admin types. Getting either wrong does not fail: it silently draws a
            // different piece of furniture.
            Dictionary<string, (int SpriteId, ProductType Type)> definitions =
                await FurnitureDefinitionLookup
                    .ResolveByClassNameAsync(
                        dbCtx,
                        offers.Select(offer => offer.ProductCode),
                        definition => (definition.SpriteId, definition.ProductType),
                        ct,
                        _logger
                    )
                    .ConfigureAwait(true);

            _offers =
            [
                .. offers.Select(offer =>
                {
                    definitions.TryGetValue(
                        offer.ProductCode,
                        out (int SpriteId, ProductType Type) definition
                    );

                    return new CachedOffer(
                        offer.Id,
                        offer.ProductCode,
                        offer.EmeraldPrice,
                        offer.IsFeatured,
                        offer.IsLimited,
                        offer.MintLimit,
                        offer.SoldCount,
                        definition.SpriteId,
                        definition.Type,
                        offer.Score,
                        offer.Rarity,
                        offer.Enabled
                    );
                }),
            ];

            _loaded = true;

            _logger.LogInformation("Loaded {OfferCount} collectibles shop offers.", _offers.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load collectibles shop offers.");
        }
    }

    private static NftStoreOfferSnapshot ToSnapshot(CachedOffer offer) =>
        new()
        {
            ProductCode = offer.ProductCode,
            EmeraldPrice = offer.EmeraldPrice,
            IsFeatured = offer.IsFeatured,
            IsLimited = offer.IsLimited,
            // Zero is this hotel's "no limit" (see IsOnSale) and -1 is the client's; sending ours
            // verbatim disables the buy button on every unlimited offer.
            MintLimit =
                offer.MintLimit <= 0 ? NftStoreOfferSnapshot.UnlimitedMintLimit : offer.MintLimit,
            MintedCount = offer.SoldCount,
            ProductInfo = new CollectibleProductItemSnapshot
            {
                ProductTypeId = CollectibleProductIdentity.ForFurniture(offer.ProductType),
                ItemTypeId = CollectibleProductIdentity.ItemTypeId(offer.SpriteId),
                Score = offer.Score,
                ProductCode = offer.ProductCode,
                Rarity = offer.Rarity,
            },
        };

    private sealed record CachedOffer(
        int Id,
        string ProductCode,
        int EmeraldPrice,
        bool IsFeatured,
        bool IsLimited,
        int MintLimit,
        int SoldCount,
        int SpriteId,
        ProductType ProductType,
        int Score,
        string Rarity,
        bool Enabled
    )
    {
        public CachedOffer Sold() => this with { SoldCount = SoldCount + 1 };
    }
}
