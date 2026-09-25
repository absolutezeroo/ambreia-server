using System;
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
/// The converted Arcturus catalog stores a decoration's pattern code only in the offer name
/// ("wallpaper_single_101", extradata ''). The client names the Spaces thumbnail after the
/// product's extra param (th_wall_101), so the snapshot must derive it -- as Arcturus did on the
/// wire -- or every tile asks for th_wall_.png and 404s. Rows seeded in that exact shape.
/// </summary>
public sealed class CatalogDecorationExtraParamTests
{
    [Theory]
    [InlineData("wallpaper_single_101", null, "101")]
    [InlineData("floor_single_110", "", "110")]
    [InlineData("landscape_single_1.10", null, "1.10")]
    [InlineData("wallpaper_single_101", "205", "205")]
    [InlineData("rare_dragonlamp", null, null)]
    public void ResolveExtraParam_DerivesTheDecorationCodeOnlyWhenItIsMissing(
        string localization,
        string? stored,
        string? expected
    ) =>
        CatalogSnapshotProvider<NormalCatalog>
            .ResolveExtraParam(stored, localization)
            .Should()
            .Be(expected);

    [Fact]
    public async Task Snapshot_CarriesTheWallpaperCode_ForAnArcturusShapedRow()
    {
        DbContextOptions<VortexDbContext> options = new DbContextOptionsBuilder<VortexDbContext>()
            .UseInMemoryDatabase($"catalog-decoration-{Guid.NewGuid():N}")
            .Options;

        int productId;

        using (VortexDbContext seed = new(options))
        {
            CatalogPageEntity page = new()
            {
                CatalogType = CatalogType.Normal,
                Localization = "spaces",
                Icon = 0,
                Layout = CatalogPageLayout.Default3x3,
                SortOrder = 0,
                Visible = true,
            };
            seed.CatalogPages.Add(page);
            seed.SaveChanges();

            CatalogOfferEntity offer = new()
            {
                CatalogPageEntityId = page.Id,
                Page = page,
                LocalizationId = "wallpaper_single_101",
                CostCredits = 3,
                CostCurrency = 0,
                CanGift = true,
                CanBundle = true,
                ClubLevel = 0,
                Visible = true,
            };
            seed.CatalogOffers.Add(offer);
            seed.SaveChanges();

            CatalogProductEntity product = new()
            {
                CatalogOfferEntityId = offer.Id,
                Offer = offer,
                ProductType = ProductType.Wall,
                ExtraParam = null,
                Quantity = 1,
                UniqueSize = 0,
                UniqueRemaining = 0,
                BuildersClubEligible = false,
            };
            seed.CatalogProducts.Add(product);
            seed.SaveChanges();
            productId = product.Id;
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

        snapshot.ProductsById[productId].ExtraParam.Should().Be("101");
    }

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
