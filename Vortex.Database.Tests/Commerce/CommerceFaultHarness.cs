using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans;
using Vortex.Catalog.Grains;
using Vortex.Events.Registry;
using Vortex.Primitives.Catalog;
using Vortex.Primitives.Catalog.Enums;
using Vortex.Primitives.Catalog.Snapshots;
using Vortex.Primitives.Events;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Inventory.Grains;
using Vortex.Primitives.Orleans.Snapshots.Players;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Players.Wallet;
using Vortex.Primitives.Rooms;
using Vortex.Tests.Support;

namespace Vortex.Database.Tests.Commerce;

/// <summary>
/// A catalog purchase running against the real inventory grain and a real database, with one step of
/// the grant armed to throw. What it asserts is the final business state — rows in the database,
/// credits in the wallet — never that a compensation method was called.
/// </summary>
internal sealed class CommerceFaultHarness : IDisposable
{
    public const int BUYER = 7;
    public const int OFFER_ID = 4200;
    public const int PRICE = 30;
    public const string BADGE_CODE = "ACH_TEST1";
    public const int EFFECT_ID = 99;

    private readonly InventoryGrainFixture _inventory = new(BUYER);

    public CommerceFaultHarness(params CatalogProductSnapshot[] products)
    {
        Offer = CatalogOffers.Offer(OFFER_ID, PRICE, products);
        Purchase = BuildPurchaseGrain();
    }

    public CatalogOfferSnapshot Offer { get; }

    public CatalogPurchaseGrain Purchase { get; }

    public CommerceFaultStep Fails
    {
        get => _inventory.Fails;
        set => _inventory.Fails = value;
    }

    public List<WalletDebitRequest> Debits { get; } = [];

    public List<WalletDebitRequest> Refunds { get; } = [];

    public IReadOnlyList<(int EffectId, int SubType, int Duration)> EffectsGranted =>
        _inventory.EffectsGranted;

    public Task<CatalogOfferSnapshot> BuyAsync(string extraParam = "", int quantity = 1) =>
        Purchase.PurchaseOfferFromCatalogAsync(
            CatalogType.Normal,
            OFFER_ID,
            extraParam,
            quantity,
            CancellationToken.None
        );

    public Task<int> FurnitureRowsAsync() => _inventory.FurnitureRowsAsync();

    public Task<int> BadgeRowsAsync() => _inventory.BadgeRowsAsync();

    public Task<int> PetRowsAsync() => _inventory.PetRowsAsync();

    public Task<int> BotRowsAsync() => _inventory.BotRowsAsync();

    public void Dispose() => _inventory.Dispose();

    private CatalogPurchaseGrain BuildPurchaseGrain()
    {
        CatalogPurchaseGrain grain = new(
            BuildPurchaseGrainFactory(),
            new StubCatalogService(Offer),
            FakeProxy.Create<IEventPublisher>(_ => Task.CompletedTask),
            NeverCancels(),
            new NullCommerceJournal(),
            NullLogger<CatalogPurchaseGrain>.Instance
        );

        GrainContexts.Install(grain, "catalogpurchase", BUYER);

        return grain;
    }

    /// <summary>A cancellable publisher with no behaviours behind it: every publish comes back with
    /// <c>Cancel</c> false, which is what the purchase path sees in production too.</summary>
    internal static ICancellableEventPublisher NeverCancels() =>
        FakeProxy.Create<ICancellableEventPublisher>(_ =>
            Task.FromResult(new EventContext { CorrelationId = string.Empty })
        );

    private IGrainFactory BuildPurchaseGrainFactory()
    {
        IPlayerWalletGrain wallet = FakeProxy.Create<IPlayerWalletGrain>(call =>
        {
            switch (call.Method.Name)
            {
                case nameof(IPlayerWalletGrain.TryDebitAsync):
                    Debits.AddRange((List<WalletDebitRequest>)call.Args![0]!);

                    return Task.FromResult(WalletDebitResult.Success());

                case nameof(IPlayerWalletGrain.CreditBackAsync):
                    Refunds.AddRange((List<WalletDebitRequest>)call.Args![0]!);

                    return Task.CompletedTask;

                default:
                    return null;
            }
        });

        IPlayerGrain player = FakeProxy.Create<IPlayerGrain>(call =>
            call.Method.Name switch
            {
                nameof(IPlayerGrain.GetMembershipAsync) => Task.FromResult(
                    new MembershipSnapshot { IsActive = false, IsVip = false }
                ),
                nameof(IPlayerGrain.TrackCreditSpendAsync) => Task.CompletedTask,
                _ => null,
            }
        );

        return FakeProxy.Create<IGrainFactory>(call =>
        {
            if (!call.Method.IsGenericMethod)
            {
                return null;
            }

            Type grainType = call.Method.GetGenericArguments()[0];

            return grainType switch
            {
                Type t when t == typeof(IPlayerWalletGrain) => wallet,
                Type t when t == typeof(IPlayerGrain) => player,
                Type t when t == typeof(IInventoryGrain) => _inventory.Grain,
                _ => null,
            };
        });
    }

    private sealed class StubCatalogService(CatalogOfferSnapshot offer) : ICatalogService
    {
        public CatalogSnapshot GetCatalogSnapshot(CatalogType catalogType) =>
            CatalogSnapshot.Empty with
            {
                OffersById = ImmutableDictionary<int, CatalogOfferSnapshot>.Empty.Add(
                    offer.Id,
                    offer
                ),
            };
    }
}

/// <summary>Offers and products for the fault-injection suite.</summary>
internal static class CatalogOffers
{
    public static CatalogOfferSnapshot Offer(
        int offerId,
        int costCredits,
        params CatalogProductSnapshot[] products
    ) =>
        new()
        {
            Id = offerId,
            PageId = 1,
            LocalizationId = "fault-offer",
            Rentable = false,
            CostCredits = costCredits,
            CostCurrency = 0,
            CurrencyTypeId = null,
            CostSilver = 0,
            CanGift = true,
            CanBundle = false,
            MembershipLevel = 0,
            Visible = true,
            ProductIds = [.. products.Select(p => p.Id)],
            Products = [.. products],
            DiscountPercent = 0,
        };

    public static CatalogProductSnapshot Product(
        int id,
        ProductType type,
        int quantity = 1,
        string? extraParam = null,
        int definitionId = InventoryGrainFixture.FLOOR_DEFINITION_ID
    ) =>
        new()
        {
            Id = id,
            OfferId = CommerceFaultHarness.OFFER_ID,
            ProductType = type,
            FurniDefinitionId = definitionId,
            SpriteId = 1,
            ExtraParam = extraParam,
            Quantity = quantity,
            UniqueSize = 0,
            UniqueRemaining = 0,
        };
}
