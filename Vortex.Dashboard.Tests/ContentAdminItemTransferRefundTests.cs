using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans;
using Vortex.Dashboard.API.Admin.Hotel;
using Vortex.Database.Context;
using Vortex.Database.Entities.Catalog;
using Vortex.Database.Entities.Furniture;
using Vortex.Database.Entities.Players;
using Vortex.Primitives.Catalog;
using Vortex.Primitives.Catalog.Enums;
using Vortex.Primitives.Content;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Inventory.Grains;
using Vortex.Primitives.Players.Enums;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Players.Providers;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Dashboard.Tests;

/// <summary>
/// The two item operations that move value rather than just destroying it: handing one to another
/// player, and paying for one taken back.
/// </summary>
/// <remarks>
/// Both hand real property to a real account, so the guards are what is tested rather than the happy
/// path alone: a transfer to an id that is nobody would leave the item owned by a player who does
/// not exist, and a refund that pays before it deletes hands out a free duplicate. Neither is
/// something the compiler, the endpoint or the audit log can catch.
/// </remarks>
public sealed class ContentAdminItemTransferRefundTests
{
    // ── Transfer ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_item_changes_hands_and_both_inventories_are_reloaded()
    {
        DbContextOptions<VortexDbContext> options = NewOptions();
        await SeedAsync(options, itemId: 600, ownerId: 7, roomId: null, alsoSeedPlayer: 8);

        ItemGrains grains = new();
        ContentAdminResult result = await Service(options, grains)
            .TransferFurnitureAsync(7, 8, 600, CancellationToken.None);

        result.Success.Should().BeTrue();

        await using VortexDbContext db = new(options);
        (await db.Furnitures.SingleAsync()).PlayerEntityId.Should().Be(8);

        // Each inventory grain caches its own list, and only the receiving one is the obvious half
        // to remember: without the first reload the giver keeps seeing an item they no longer own.
        grains.Reloaded.Should().BeEquivalentTo([7L, 8L]);
    }

    [Fact]
    public async Task A_placed_item_is_not_handed_over()
    {
        DbContextOptions<VortexDbContext> options = NewOptions();
        await SeedAsync(options, itemId: 601, ownerId: 7, roomId: 42, alsoSeedPlayer: 8);

        ItemGrains grains = new();
        ContentAdminResult result = await Service(options, grains)
            .TransferFurnitureAsync(7, 8, 601, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("item_is_placed");

        await using VortexDbContext db = new(options);
        (await db.Furnitures.SingleAsync())
            .PlayerEntityId.Should()
            .Be(7, "the room still shows it under its owner's name");
        grains.Reloaded.Should().BeEmpty();
    }

    [Fact]
    public async Task A_recipient_that_does_not_exist_is_refused()
    {
        DbContextOptions<VortexDbContext> options = NewOptions();
        await SeedAsync(options, itemId: 602, ownerId: 7, roomId: null, alsoSeedPlayer: null);

        // Nothing else checks this: the column takes any int, so without the guard the item ends up
        // owned by an account that does not exist and nobody can ever get it back.
        ItemGrains grains = new();
        ContentAdminResult result = await Service(options, grains)
            .TransferFurnitureAsync(7, 9999, 602, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("recipient_not_found");

        await using VortexDbContext db = new(options);
        (await db.Furnitures.SingleAsync()).PlayerEntityId.Should().Be(7);
    }

    [Fact]
    public async Task An_item_belonging_to_someone_else_is_not_handed_over()
    {
        DbContextOptions<VortexDbContext> options = NewOptions();
        await SeedAsync(options, itemId: 603, ownerId: 7, roomId: null, alsoSeedPlayer: 8);

        ContentAdminResult result = await Service(options, new ItemGrains())
            .TransferFurnitureAsync(9, 8, 603, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("item_not_owned");

        await using VortexDbContext db = new(options);
        (await db.Furnitures.SingleAsync()).PlayerEntityId.Should().Be(7);
    }

    [Fact]
    public async Task Handing_an_item_to_its_own_owner_is_refused()
    {
        DbContextOptions<VortexDbContext> options = NewOptions();
        await SeedAsync(options, itemId: 604, ownerId: 7, roomId: null, alsoSeedPlayer: null);

        ItemGrains grains = new();
        ContentAdminResult result = await Service(options, grains)
            .TransferFurnitureAsync(7, 7, 604, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("same_player");
        grains.Reloaded.Should().BeEmpty("a no-op should not tell a client its inventory changed");
    }

    // ── Refund ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_cheapest_catalogue_price_is_paid_and_the_item_is_gone()
    {
        DbContextOptions<VortexDbContext> options = NewOptions();
        await SeedAsync(options, itemId: 610, ownerId: 7, roomId: null, alsoSeedPlayer: null);

        // Two offers sell the same definition. The cheaper one is the refund: a definition that also
        // appears inside an expensive bundle must not pay out at the bundle's price.
        await SeedOffersAsync(options, definitionId: 1, costs: [30, 8]);

        ItemGrains grains = new();
        ContentAdminResult result = await Service(options, grains)
            .RefundFurnitureAsync(7, 610, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Id.Should().Be(8, "the operator is told what was actually paid");
        grains.Credited.Should().Equal(8);
        grains.Reloaded.Should().Equal(7L);

        await using VortexDbContext db = new(options);
        (await db.Furnitures.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task The_item_is_already_gone_when_the_credit_is_paid()
    {
        DbContextOptions<VortexDbContext> options = NewOptions();
        await SeedAsync(options, itemId: 611, ownerId: 7, roomId: null, alsoSeedPlayer: null);
        await SeedOffersAsync(options, definitionId: 1, costs: [12]);

        // The ordering is the whole guard. Paying first and then failing to delete is the one
        // sequence that leaves the player holding both the item and its price.
        int itemsAtPayout = -1;
        ItemGrains grains = new()
        {
            OnCredit = () =>
            {
                using VortexDbContext db = new(options);
                itemsAtPayout = db.Furnitures.Count();
            },
        };

        await Service(options, grains).RefundFurnitureAsync(7, 611, CancellationToken.None);

        itemsAtPayout.Should().Be(0, "the row is committed gone before the wallet is credited");
    }

    [Fact]
    public async Task An_item_the_catalogue_does_not_sell_is_refused()
    {
        DbContextOptions<VortexDbContext> options = NewOptions();
        await SeedAsync(options, itemId: 612, ownerId: 7, roomId: null, alsoSeedPlayer: null);

        // No offer at all: a traded, won or granted item has no price to give back, and paying zero
        // silently would be worse than saying so.
        ItemGrains grains = new();
        ContentAdminResult result = await Service(options, grains)
            .RefundFurnitureAsync(7, 612, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("no_catalogue_price");
        grains.Credited.Should().BeEmpty();

        await using VortexDbContext db = new(options);
        (await db.Furnitures.CountAsync()).Should().Be(1, "a refused refund keeps the item");
    }

    [Fact]
    public async Task A_placed_item_is_not_refunded()
    {
        DbContextOptions<VortexDbContext> options = NewOptions();
        await SeedAsync(options, itemId: 613, ownerId: 7, roomId: 42, alsoSeedPlayer: null);
        await SeedOffersAsync(options, definitionId: 1, costs: [12]);

        ItemGrains grains = new();
        ContentAdminResult result = await Service(options, grains)
            .RefundFurnitureAsync(7, 613, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("item_is_placed");
        grains.Credited.Should().BeEmpty();
    }

    [Fact]
    public async Task An_item_belonging_to_someone_else_is_not_refunded()
    {
        DbContextOptions<VortexDbContext> options = NewOptions();
        await SeedAsync(options, itemId: 614, ownerId: 7, roomId: null, alsoSeedPlayer: null);
        await SeedOffersAsync(options, definitionId: 1, costs: [12]);

        // Here the owner check is not only about deleting the wrong item: the credits go to whoever
        // the caller named, so a typo would pay a stranger for someone else's furniture.
        ItemGrains grains = new();
        ContentAdminResult result = await Service(options, grains)
            .RefundFurnitureAsync(9, 614, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("item_not_owned");
        grains.Credited.Should().BeEmpty();

        await using VortexDbContext db = new(options);
        (await db.Furnitures.CountAsync()).Should().Be(1);
    }

    // ── Harness ──────────────────────────────────────────────────────────────

    private static DbContextOptions<VortexDbContext> NewOptions() =>
        new DbContextOptionsBuilder<VortexDbContext>()
            .UseInMemoryDatabase($"item-move-{Guid.NewGuid():N}")
            .Options;

    private static async Task SeedAsync(
        DbContextOptions<VortexDbContext> options,
        int itemId,
        int ownerId,
        int? roomId,
        int? alsoSeedPlayer
    )
    {
        await using VortexDbContext db = new(options);

        db.Furnitures.Add(
            new FurnitureEntity
            {
                Id = itemId,
                PlayerEntityId = ownerId,
                FurnitureDefinitionEntityId = 1,
                RoomEntityId = roomId,
            }
        );

        if (alsoSeedPlayer is not null)
        {
            db.Players.Add(NewPlayer(alsoSeedPlayer.Value));
        }

        await db.SaveChangesAsync();
    }

    /// <summary>One page, one offer per price, each selling <paramref name="definitionId"/>.</summary>
    private static async Task SeedOffersAsync(
        DbContextOptions<VortexDbContext> options,
        int definitionId,
        int[] costs
    )
    {
        await using VortexDbContext db = new(options);

        CatalogPageEntity page = new()
        {
            CatalogType = CatalogType.Normal,
            Localization = "page",
            Icon = 1,
            Layout = CatalogPageLayout.Default3x3,
            SortOrder = 1,
            Visible = true,
        };

        db.CatalogPages.Add(page);

        foreach (int cost in costs)
        {
            CatalogOfferEntity offer = new()
            {
                CatalogPageEntityId = page.Id,
                Page = page,
                LocalizationId = $"offer-{cost}",
                CostCredits = cost,
                CostCurrency = 0,
                CanGift = true,
                CanBundle = false,
                ClubLevel = 0,
                Visible = true,
            };

            db.CatalogOffers.Add(offer);
            db.CatalogProducts.Add(
                new CatalogProductEntity
                {
                    CatalogOfferEntityId = offer.Id,
                    Offer = offer,
                    ProductType = ProductType.Floor,
                    FurnitureDefinitionEntityId = definitionId,
                    Quantity = 1,
                    UniqueSize = 0,
                    UniqueRemaining = 0,
                }
            );
        }

        await db.SaveChangesAsync();
    }

    private static PlayerEntity NewPlayer(int id) =>
        new()
        {
            Id = id,
            Name = $"player-{id}",
            Motto = string.Empty,
            Figure = "hd-180-1",
            Gender = AvatarGenderType.Male,
            PlayerStatus = PlayerStatusType.Offline,
            PlayerPerks = PlayerPerkFlags.None,
        };

    /// <summary>
    /// What the service told the runtime: which inventories it reloaded, and what it paid.
    /// </summary>
    private sealed class ItemGrains
    {
        public List<long> Reloaded { get; } = [];

        public List<int> Credited { get; } = [];

        /// <summary>Runs while the credit call is in flight, to look at the database as it is at
        /// that exact moment.</summary>
        public Action? OnCredit { get; init; }
    }

    private static ContentAdminService Service(
        DbContextOptions<VortexDbContext> options,
        ItemGrains grains
    )
    {
        // GetGrain<T> is generic, so the interface being asked for is the call's return type and the
        // player id is its first argument -- which is how one fake factory answers for both grains.
        IGrainFactory factory = FakeProxy.Create<IGrainFactory>(call =>
            call.Method.ReturnType == typeof(IInventoryGrain)
                ? Inventory(grains, (long)call.Args![0]!)
                : Wallet(grains)
        );

        return new ContentAdminService(
            new TestContextFactory(options),
            factory,
            FakeProxy.Create<ICurrencyTypeProvider>(_ => null!),
            NullLogger<ContentAdminService>.Instance
        );
    }

    private static IInventoryGrain Inventory(ItemGrains grains, long playerId) =>
        FakeProxy.Create<IInventoryGrain>(call =>
        {
            if (call.Method.Name == nameof(IInventoryGrain.ReloadFurnitureAsync))
            {
                grains.Reloaded.Add(playerId);
            }

            return null;
        });

    private static IPlayerWalletGrain Wallet(ItemGrains grains) =>
        FakeProxy.Create<IPlayerWalletGrain>(call =>
        {
            if (call.Method.Name == nameof(IPlayerWalletGrain.GrantCreditsAsync))
            {
                grains.Credited.Add((int)call.Args![0]!);
                grains.OnCredit?.Invoke();
            }

            return null;
        });

    private sealed class TestContextFactory(DbContextOptions<VortexDbContext> options)
        : IDbContextFactory<VortexDbContext>
    {
        public VortexDbContext CreateDbContext() => new(options);
    }
}
