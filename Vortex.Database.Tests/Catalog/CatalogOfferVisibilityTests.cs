using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Vortex.Catalog.Providers;
using Vortex.Database.Context;
using Vortex.Database.Entities.Catalog;
using Vortex.Primitives.Catalog;
using Vortex.Primitives.Catalog.Enums;
using Vortex.Primitives.Catalog.Providers;
using Vortex.Primitives.Catalog.Snapshots;
using Vortex.Primitives.Catalog.Tags;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Furniture.Snapshots;
using Xunit;

namespace Vortex.Database.Tests.Catalog;

/// <summary>
/// catalog_offers.visible hides an offer from the page that owns it -- both the catalog index and
/// GetCatalogPage list a page's contents straight from CatalogPageSnapshot.OfferIds, so the flag is
/// only ever honoured if the snapshot leaves hidden offers out of that list. It must stay resolvable
/// by id (OffersById), which is how a purchase, a bundle and a front-page item reach it.
/// </summary>
public sealed class CatalogOfferVisibilityTests
{
    [Fact]
    public async Task HiddenOffer_IsNotListedOnItsPage_ButStaysResolvableById()
    {
        DbContextOptions<VortexDbContext> options = new DbContextOptionsBuilder<VortexDbContext>()
            .UseInMemoryDatabase($"catalog-visibility-{Guid.NewGuid():N}")
            .Options;

        int visibleOfferId;
        int hiddenOfferId;
        int pageId;

        using (VortexDbContext seed = new(options))
        {
            CatalogPageEntity page = new()
            {
                CatalogType = CatalogType.Normal,
                Localization = "page",
                Icon = 0,
                Layout = CatalogPageLayout.Default3x3,
                SortOrder = 0,
                Visible = true,
            };
            seed.CatalogPages.Add(page);
            seed.SaveChanges();
            pageId = page.Id;

            CatalogOfferEntity visible = NewOffer(page, "visible_offer", visible: true);
            CatalogOfferEntity hidden = NewOffer(page, "hidden_offer", visible: false);
            seed.CatalogOffers.Add(visible);
            seed.CatalogOffers.Add(hidden);
            seed.SaveChanges();
            visibleOfferId = visible.Id;
            hiddenOfferId = hidden.Id;
        }

        CatalogSnapshotProvider<NormalCatalog> provider = new(
            new TestDbContextFactory(options),
            NullLogger<ICatalogSnapshotProvider<NormalCatalog>>.Instance,
            new NullDefinitionProvider(),
            CatalogType.Normal
        );

        CatalogSnapshot snapshot = await provider
            .GetSnapshotAsync(CancellationToken.None)
            .ConfigureAwait(true);

        ImmutableArray<int> listed = snapshot.PagesById[pageId].OfferIds;
        listed.Should().Contain(visibleOfferId);
        listed.Should().NotContain(hiddenOfferId);

        snapshot.OffersById.Should().ContainKey(hiddenOfferId);
        snapshot.OffersById[hiddenOfferId].Visible.Should().BeFalse();
    }

    /// <summary>
    /// A product type nothing grants any more (player bots, the removed chat styles) must not be
    /// sold: the planner skips it, so the buyer would be charged for nothing. The imported catalogue
    /// still carries such rows.
    /// </summary>
    [Fact]
    public async Task AProductNothingGrants_IsDropped_AndAnOfferLeftEmpty_IsNotSold()
    {
        DbContextOptions<VortexDbContext> options = new DbContextOptionsBuilder<VortexDbContext>()
            .UseInMemoryDatabase($"catalog-unsold-{Guid.NewGuid():N}")
            .Options;

        int pageId;
        int botOfferId;
        int chatStyleOfferId;
        int mixedOfferId;

        using (VortexDbContext seed = new(options))
        {
            CatalogPageEntity page = new()
            {
                CatalogType = CatalogType.Normal,
                Localization = "page",
                Icon = 0,
                Layout = CatalogPageLayout.Default3x3,
                SortOrder = 0,
                Visible = true,
            };
            seed.CatalogPages.Add(page);
            seed.SaveChanges();
            pageId = page.Id;

            CatalogOfferEntity bot = NewOffer(page, "bot_offer", visible: true);
            CatalogOfferEntity chatStyle = NewOffer(page, "chat_style_offer", visible: true);
            CatalogOfferEntity mixed = NewOffer(page, "mixed_offer", visible: true);
            seed.CatalogOffers.Add(bot);
            seed.CatalogOffers.Add(chatStyle);
            seed.CatalogOffers.Add(mixed);
            seed.SaveChanges();
            botOfferId = bot.Id;
            chatStyleOfferId = chatStyle.Id;
            mixedOfferId = mixed.Id;

            seed.CatalogProducts.Add(NewProduct(bot, ProductType.Robot));
            seed.CatalogProducts.Add(NewProduct(chatStyle, (ProductType)7));
            seed.CatalogProducts.Add(NewProduct(mixed, ProductType.Floor));
            seed.CatalogProducts.Add(NewProduct(mixed, ProductType.Robot));
            seed.SaveChanges();
        }

        CatalogSnapshotProvider<NormalCatalog> provider = new(
            new TestDbContextFactory(options),
            NullLogger<ICatalogSnapshotProvider<NormalCatalog>>.Instance,
            new NullDefinitionProvider(),
            CatalogType.Normal
        );

        CatalogSnapshot snapshot = await provider
            .GetSnapshotAsync(CancellationToken.None)
            .ConfigureAwait(true);

        snapshot.PagesById[pageId].OfferIds.Should().Equal(mixedOfferId);
        snapshot.OffersById.Should().NotContainKey(botOfferId);
        snapshot.OffersById.Should().NotContainKey(chatStyleOfferId);
        snapshot
            .OffersById[mixedOfferId]
            .Products.Should()
            .ContainSingle()
            .Which.ProductType.Should()
            .Be(ProductType.Floor);
    }

    private static CatalogProductEntity NewProduct(CatalogOfferEntity offer, ProductType type) =>
        new()
        {
            CatalogOfferEntityId = offer.Id,
            Offer = offer,
            ProductType = type,
            ExtraParam = string.Empty,
            Quantity = 1,
            UniqueSize = 0,
            UniqueRemaining = 0,
        };

    private static CatalogOfferEntity NewOffer(
        CatalogPageEntity page,
        string localizationId,
        bool visible
    ) =>
        new()
        {
            CatalogPageEntityId = page.Id,
            Page = page,
            LocalizationId = localizationId,
            CostCredits = 1,
            CostCurrency = 0,
            CanGift = true,
            CanBundle = true,
            MembershipLevel = 0,
            Visible = visible,
        };

    private sealed class TestDbContextFactory(DbContextOptions<VortexDbContext> options)
        : IDbContextFactory<VortexDbContext>
    {
        public VortexDbContext CreateDbContext() => new(options);
    }

    private sealed class NullDefinitionProvider : IFurnitureDefinitionProvider
    {
        public FurnitureDefinitionSnapshot? TryGetDefinition(int id) => null;

        public FurnitureDefinitionSnapshot? TryGetDefinitionByName(string name) => null;

        public Task ReloadAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
