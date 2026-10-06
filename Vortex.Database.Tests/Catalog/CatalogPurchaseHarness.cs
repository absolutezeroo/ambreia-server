using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans;
using Orleans.Runtime;
using Vortex.Catalog.Grains;
using Vortex.Events.Registry;
using Vortex.Primitives.Catalog;
using Vortex.Primitives.Catalog.Enums;
using Vortex.Primitives.Catalog.Snapshots;
using Vortex.Primitives.Events;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Inventory.Grains;
using Vortex.Primitives.Orleans.Snapshots.Players;
using Vortex.Primitives.Players.Enums.Wallet;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Players.Wallet;
using Vortex.Primitives.Rooms;
using Vortex.Tests.Support;

namespace Vortex.Database.Tests.Catalog;

/// <summary>
/// Builds a <see cref="CatalogPurchaseGrain"/> outside a silo. The grain reads its own primary key,
/// so the activation context is stubbed rather than left null; every collaborator is a recording
/// proxy so the assertions can be about who was charged, who was granted, and how much of each.
/// Shared by the gift suite and the plain-purchase suite because both entry points fund themselves
/// from the same wallet through the same shared primitive — a harness per entry point would have
/// let their guarantees drift apart, which is the whole thing these tests are here to prevent.
/// </summary>
internal sealed class CatalogPurchaseHarness
{
    public CatalogPurchaseHarness(
        CatalogOfferSnapshot offer,
        int buyerId,
        CatalogProductSnapshot? product = null
    )
    {
        Offer = offer;
        BuyerId = buyerId;
        Product = product;
        Grain = BuildGrain();
    }

    /// <summary>The offer's single product, when a test needs one. Room ads read their duration
    /// from the product's Quantity rather than from a configured field, so that arithmetic is only
    /// reachable through here.</summary>
    public CatalogProductSnapshot? Product { get; }

    public CatalogOfferSnapshot Offer { get; }

    public int BuyerId { get; }

    public CatalogPurchaseGrain Grain { get; }

    /// <summary>What the flow told the journal. Enough to answer "did it journal at all".</summary>
    public Commerce.NullCommerceJournal Journal { get; } = new();

    public ClubSubscriptionSnapshot Club { get; set; } =
        new ClubSubscriptionSnapshot { IsActive = false, IsVip = false };

    public bool GrantThrows { get; set; }

    public bool DebitSucceeds { get; set; } = true;

    /// <summary>Whose inventory the offer was actually granted into, read from the grain key the
    /// purchase asked the factory for rather than assumed.</summary>
    public List<int> GrantedToPlayerIds { get; } = [];

    /// <summary>The quantity that reached the inventory. The cost and the number of copies are
    /// computed from the same wire field, so a test that only checks the charge would miss a
    /// purchase that charged for one and delivered a million.</summary>
    public List<int> GrantedQuantities { get; } = [];

    public List<int> DebitedPlayerIds { get; } = [];

    public List<WalletDebitRequest> DebitRequests { get; } = [];

    public List<int> TrackedCreditSpend { get; } = [];

    public List<IEvent> Events { get; } = [];

    /// <summary>Room advertisements the purchase created: (roomId, name, expiry). The duration a
    /// room ad buys is encoded in the product rather than configured, so what lands here is the
    /// only place that arithmetic is observable.</summary>
    /// <summary>Stands in for the behaviours a plugin would register on
    /// <see cref="Vortex.Primitives.Events.CatalogPurchasingEvent" />: set before buying and the
    /// grain sees its pre-purchase publish come back cancelled.</summary>
    public bool CancelPurchase { get; set; }

    public int CreditBackCalls { get; private set; }

    private int _lastInventoryKey;

    private CatalogPurchaseGrain BuildGrain()
    {
        CatalogPurchaseGrain grain = new CatalogPurchaseGrain(
            BuildGrainFactory(),
            new StubCatalogService(Offer, Product),
            new RecordingEventPublisher(Events),
            FakeProxy.Create<ICancellableEventPublisher>(call =>
            {
                Events.Add((IEvent)call.Args![0]!);

                return Task.FromResult(
                    new EventContext { CorrelationId = string.Empty, Cancel = CancelPurchase }
                );
            }),
            Journal,
            NullLogger<CatalogPurchaseGrain>.Instance
        );

        SetGrainContext(grain, BuyerId);

        return grain;
    }

    private IGrainFactory BuildGrainFactory()
    {
        IPlayerWalletGrain wallet = FakeProxy.Create<IPlayerWalletGrain>(call =>
        {
            switch (call.Method.Name)
            {
                case nameof(IPlayerWalletGrain.TryDebitAsync):
                    DebitedPlayerIds.Add(BuyerId);

                    if (!DebitSucceeds)
                    {
                        return Task.FromResult(
                            WalletDebitResult.InsufficientBalance(
                                new WalletDebitFailure
                                {
                                    CurrencyKind = new CurrencyKind
                                    {
                                        CurrencyType = CurrencyType.Credits,
                                    },
                                    Amount = Offer.CostCredits,
                                }
                            )
                        );
                    }

                    DebitRequests.AddRange((List<WalletDebitRequest>)call.Args![0]!);

                    return Task.FromResult(WalletDebitResult.Success());

                case nameof(IPlayerWalletGrain.CreditBackAsync):
                    CreditBackCalls++;

                    return Task.CompletedTask;

                default:
                    return null;
            }
        });

        IPlayerGrain player = FakeProxy.Create<IPlayerGrain>(call =>
            call.Method.Name switch
            {
                nameof(IPlayerGrain.GetClubSubscriptionAsync) => Task.FromResult(Club),
                nameof(IPlayerGrain.TrackCreditSpendAsync) => TrackAsync((int)call.Args![0]!),
                _ => null,
            }
        );

        IInventoryGrain inventory = FakeProxy.Create<IInventoryGrain>(call =>
        {
            if (call.Method.Name != nameof(IInventoryGrain.GrantCatalogOfferAsync))
            {
                return null;
            }

            if (GrantThrows)
            {
                throw new InvalidOperationException("grant failed");
            }

            GrantedToPlayerIds.Add(_lastInventoryKey);
            GrantedQuantities.Add((int)call.Args![2]!);

            return Task.CompletedTask;
        });

        return FakeProxy.Create<IGrainFactory>(call =>
        {
            if (!call.Method.IsGenericMethod)
            {
                return null;
            }

            Type grainType = call.Method.GetGenericArguments()[0];

            if (grainType == typeof(IInventoryGrain))
            {
                // The purchase picks the inventory it grants into by grain key; capturing it here is
                // what lets a test tell "granted to the buyer" from "granted to the recipient".
                _lastInventoryKey = Convert.ToInt32(call.Args![0]!);

                return inventory;
            }

            return grainType switch
            {
                Type t when t == typeof(IPlayerWalletGrain) => wallet,
                Type t when t == typeof(IPlayerGrain) => player,
                _ => null,
            };
        });
    }

    private Task TrackAsync(int credits)
    {
        TrackedCreditSpend.Add(credits);

        return Task.CompletedTask;
    }

    /// <summary>The grain calls <c>this.GetPrimaryKeyLong()</c>, which resolves through the
    /// activation context Orleans would normally install.</summary>
    private static void SetGrainContext(Grain grain, int playerId)
    {
        GrainId grainId = GrainId.Create(
            GrainType.Create("catalogpurchase"),
            GrainIdKeyExtensions.CreateIntegerKey(playerId)
        );

        IGrainContext context = FakeProxy.Create<IGrainContext>(call =>
            call.Method.Name == $"get_{nameof(IGrainContext.GrainId)}" ? grainId : null
        );

        FieldInfo field =
            typeof(Grain).GetField(
                "<GrainContext>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic
            ) ?? throw new InvalidOperationException("Grain.GrainContext backing field moved.");

        field.SetValue(grain, context);
    }

    private sealed class StubCatalogService(
        CatalogOfferSnapshot offer,
        CatalogProductSnapshot? product
    ) : ICatalogService
    {
        public CatalogSnapshot GetCatalogSnapshot(CatalogType catalogType) =>
            CatalogSnapshot.Empty with
            {
                OffersById = ImmutableDictionary<int, CatalogOfferSnapshot>.Empty.Add(
                    offer.Id,
                    offer
                ),
                ProductsById = product is null
                    ? ImmutableDictionary<int, CatalogProductSnapshot>.Empty
                    : ImmutableDictionary<int, CatalogProductSnapshot>.Empty.Add(
                        product.Id,
                        product
                    ),
                OfferProductIds = product is null
                    ? ImmutableDictionary<int, ImmutableArray<int>>.Empty
                    : ImmutableDictionary<int, ImmutableArray<int>>.Empty.Add(
                        offer.Id,
                        [product.Id]
                    ),
            };
    }

    private sealed class RecordingEventPublisher(List<IEvent> sink) : IEventPublisher
    {
        public Task PublishAsync(IEvent @event, CancellationToken ct = default)
        {
            sink.Add(@event);

            return Task.CompletedTask;
        }
    }
}

/// <summary>Offer builder shared by the catalog purchase suites.</summary>
internal static class CatalogOffers
{
    public static CatalogOfferSnapshot New(
        int offerId,
        int costCredits,
        int clubLevel = 0,
        int discountPercent = 0,
        bool canGift = true,
        int costSilver = 0,
        int costCurrency = 0,
        int? currencyTypeId = null
    ) =>
        new()
        {
            Id = offerId,
            PageId = 1,
            LocalizationId = "offer",
            Rentable = false,
            CostCredits = costCredits,
            CostCurrency = costCurrency,
            CurrencyTypeId = currencyTypeId,
            CostSilver = costSilver,
            CanGift = canGift,
            CanBundle = false,
            ClubLevel = clubLevel,
            Visible = true,
            ProductIds = [],
            Products = [],
            DiscountPercent = discountPercent,
        };

    public static CatalogProductSnapshot NewProduct(int offerId, int quantity) =>
        new()
        {
            Id = 1,
            OfferId = offerId,
            ProductType = ProductType.Floor,
            FurniDefinitionId = 1,
            SpriteId = 1,
            ExtraParam = null,
            Quantity = quantity,
            UniqueSize = 0,
            UniqueRemaining = 0,
        };
}
