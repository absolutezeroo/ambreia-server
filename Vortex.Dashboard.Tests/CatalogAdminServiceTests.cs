using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Vortex.Dashboard.API.Admin.Catalogue;
using Vortex.Database.Context;
using Vortex.Database.Entities.Catalog;
using Vortex.Primitives.Catalog;
using Vortex.Primitives.Catalog.Admin;
using Vortex.Primitives.Catalog.Enums;
using Vortex.Primitives.Catalog.Providers;
using Vortex.Primitives.Catalog.Snapshots;
using Vortex.Primitives.Catalog.Tags;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Players.Enums.Wallet;
using Xunit;

namespace Vortex.Dashboard.Tests;

/// <summary>
/// Covers the invariants <c>CatalogAdminService</c> is responsible for: the delete-blocked-with-
/// children guards that prevent a silent cascading mass-delete, and that every successful write
/// reloads the snapshot provider -- the exact "DB write not reflected in live state" bug class
/// called out in AGENTS.md.
/// </summary>
public sealed class CatalogAdminServiceTests
{
    private static VortexDbContext NewContext(DbContextOptions<VortexDbContext> options) =>
        new(options);

    private static DbContextOptions<VortexDbContext> NewOptions() =>
        new DbContextOptionsBuilder<VortexDbContext>()
            .UseInMemoryDatabase($"catalog-admin-{Guid.NewGuid():N}")
            .Options;

    private static CatalogAdminService NewService(
        DbContextOptions<VortexDbContext> options,
        out FakeSnapshotProvider<NormalCatalog> normalProvider
    )
    {
        normalProvider = new FakeSnapshotProvider<NormalCatalog>(CatalogType.Normal);

        return new CatalogAdminService(
            new TestDbContextFactory(options),
            normalProvider,
            NullLogger<CatalogAdminService>.Instance
        );
    }

    [Fact]
    public async Task CreatePage_Succeeds_AndReloadsTheSnapshotProvider()
    {
        DbContextOptions<VortexDbContext> options = NewOptions();
        CatalogAdminService service = NewService(
            options,
            out FakeSnapshotProvider<NormalCatalog> normalProvider
        );

        CatalogAdminResult result = await service
            .CreatePageAsync(
                new CatalogPageCreateSpec(
                    CatalogType.Normal,
                    null,
                    "shop_root",
                    "Shop",
                    0,
                    CatalogPageLayout.Default3x3,
                    null,
                    null,
                    0,
                    true
                ),
                CancellationToken.None
            )
            .ConfigureAwait(true);

        result.Success.Should().BeTrue();
        result.Id.Should().NotBeNull();

        await using VortexDbContext verify = NewContext(options);
        (await verify.CatalogPages.FindAsync(result.Id!.Value).ConfigureAwait(true))
            .Should()
            .NotBeNull();

        normalProvider.ReloadCount.Should().Be(1);
    }

    [Fact]
    public async Task DeletePage_WithChildren_IsBlocked()
    {
        DbContextOptions<VortexDbContext> options = NewOptions();
        CatalogAdminService service = NewService(options, out _);

        CatalogAdminResult parent = await service
            .CreatePageAsync(
                new CatalogPageCreateSpec(
                    CatalogType.Normal,
                    null,
                    "parent",
                    null,
                    0,
                    CatalogPageLayout.Default3x3,
                    null,
                    null,
                    0,
                    true
                ),
                CancellationToken.None
            )
            .ConfigureAwait(true);

        await service
            .CreatePageAsync(
                new CatalogPageCreateSpec(
                    CatalogType.Normal,
                    parent.Id,
                    "child",
                    null,
                    0,
                    CatalogPageLayout.Default3x3,
                    null,
                    null,
                    0,
                    true
                ),
                CancellationToken.None
            )
            .ConfigureAwait(true);

        CatalogAdminResult deleteResult = await service
            .DeletePageAsync(parent.Id!.Value, CancellationToken.None)
            .ConfigureAwait(true);

        deleteResult.Success.Should().BeFalse();
        deleteResult.ErrorCode.Should().Be("page_has_children");
    }

    /// <summary>
    /// A soft-deleted child is invisible to the children guard — the global <c>DeletedAt</c> filter
    /// hides it — but <c>parent_id</c> still points at the page, so MySQL refused the delete with a
    /// foreign-key error the operator could do nothing about: the dashboard shows no child to move.
    /// The dead child is detached instead, and the page goes.
    /// </summary>
    [Fact]
    public async Task DeletePage_WithOnlySoftDeletedChildren_DetachesThemAndDeletes()
    {
        DbContextOptions<VortexDbContext> options = NewOptions();
        CatalogAdminService service = NewService(options, out _);

        CatalogAdminResult parent = await service
            .CreatePageAsync(
                new CatalogPageCreateSpec(
                    CatalogType.Normal,
                    null,
                    "parent",
                    null,
                    0,
                    CatalogPageLayout.Default3x3,
                    null,
                    null,
                    0,
                    true
                ),
                CancellationToken.None
            )
            .ConfigureAwait(true);

        CatalogAdminResult child = await service
            .CreatePageAsync(
                new CatalogPageCreateSpec(
                    CatalogType.Normal,
                    parent.Id,
                    "dead_child",
                    null,
                    0,
                    CatalogPageLayout.Default3x3,
                    null,
                    null,
                    0,
                    true
                ),
                CancellationToken.None
            )
            .ConfigureAwait(true);

        await using (VortexDbContext seed = NewContext(options))
        {
            (await seed.CatalogPages.FindAsync(child.Id!.Value).ConfigureAwait(true))!.DeletedAt =
                DateTime.UtcNow;
            await seed.SaveChangesAsync().ConfigureAwait(true);
        }

        CatalogAdminResult deleteResult = await service
            .DeletePageAsync(parent.Id!.Value, CancellationToken.None)
            .ConfigureAwait(true);

        deleteResult.Success.Should().BeTrue();

        await using VortexDbContext verify = NewContext(options);
        (
            await verify
                .CatalogPages.IgnoreQueryFilters()
                .SingleAsync(p => p.Id == child.Id!.Value)
                .ConfigureAwait(true)
        )
            .ParentEntityId.Should()
            .BeNull();
    }

    /// <summary>
    /// An offer's currency is the activity-point type the purchase debits and the client reads
    /// (5 = diamonds), not a currency_types row id: the diamonds row here is id 3, and the offer
    /// must store 5.
    /// </summary>
    [Theory]
    [InlineData(5, true)]
    [InlineData(3, false)]
    public async Task CreateOffer_CurrencyIsTheActivityPointType(int currency, bool accepted)
    {
        DbContextOptions<VortexDbContext> options = NewOptions();

        await using (VortexDbContext db = NewContext(options))
        {
            db.CurrencyTypes.Add(
                new CurrencyTypeEntity
                {
                    Id = 3,
                    Name = "Diamonds",
                    CurrencyType = CurrencyType.ActivityPoints,
                    ActivityPointType = 5,
                    Enabled = true,
                }
            );
            await db.SaveChangesAsync().ConfigureAwait(true);
        }

        CatalogAdminService service = NewService(options, out _);

        CatalogAdminResult page = await service
            .CreatePageAsync(
                new CatalogPageCreateSpec(
                    CatalogType.Normal,
                    null,
                    "diamond_page",
                    null,
                    0,
                    CatalogPageLayout.Default3x3,
                    null,
                    null,
                    0,
                    true
                ),
                CancellationToken.None
            )
            .ConfigureAwait(true);

        CatalogAdminResult offer = await service
            .CreateOfferAsync(
                new CatalogOfferCreateSpec(
                    page.Id!.Value,
                    "offer_loc",
                    0,
                    25,
                    currency,
                    true,
                    true,
                    0,
                    0,
                    true
                ),
                CancellationToken.None
            )
            .ConfigureAwait(true);

        offer.Success.Should().Be(accepted);
    }

    [Fact]
    public async Task DeletePage_WithOffers_IsBlocked()
    {
        DbContextOptions<VortexDbContext> options = NewOptions();
        CatalogAdminService service = NewService(options, out _);

        CatalogAdminResult page = await service
            .CreatePageAsync(
                new CatalogPageCreateSpec(
                    CatalogType.Normal,
                    null,
                    "page_with_offer",
                    null,
                    0,
                    CatalogPageLayout.Default3x3,
                    null,
                    null,
                    0,
                    true
                ),
                CancellationToken.None
            )
            .ConfigureAwait(true);

        await service
            .CreateOfferAsync(
                new CatalogOfferCreateSpec(
                    page.Id!.Value,
                    "offer_loc",
                    10,
                    0,
                    null,
                    true,
                    true,
                    0,
                    0,
                    true
                ),
                CancellationToken.None
            )
            .ConfigureAwait(true);

        CatalogAdminResult deleteResult = await service
            .DeletePageAsync(page.Id.Value, CancellationToken.None)
            .ConfigureAwait(true);

        deleteResult.Success.Should().BeFalse();
        deleteResult.ErrorCode.Should().Be("page_has_offers");
    }

    [Fact]
    public async Task DeleteOffer_WithProducts_IsBlocked_ThenSucceedsAfterProductRemoved()
    {
        DbContextOptions<VortexDbContext> options = NewOptions();
        CatalogAdminService service = NewService(options, out _);

        CatalogAdminResult page = await service
            .CreatePageAsync(
                new CatalogPageCreateSpec(
                    CatalogType.Normal,
                    null,
                    "page",
                    null,
                    0,
                    CatalogPageLayout.Default3x3,
                    null,
                    null,
                    0,
                    true
                ),
                CancellationToken.None
            )
            .ConfigureAwait(true);

        CatalogAdminResult offer = await service
            .CreateOfferAsync(
                new CatalogOfferCreateSpec(
                    page.Id!.Value,
                    "offer_loc",
                    10,
                    0,
                    null,
                    true,
                    true,
                    0,
                    0,
                    true
                ),
                CancellationToken.None
            )
            .ConfigureAwait(true);

        CatalogAdminResult product = await service
            .CreateProductAsync(
                new CatalogProductCreateSpec(
                    offer.Id!.Value,
                    ProductType.Floor,
                    null,
                    null,
                    1,
                    0,
                    0
                ),
                CancellationToken.None
            )
            .ConfigureAwait(true);

        product.Success.Should().BeTrue();

        CatalogAdminResult blockedDelete = await service
            .DeleteOfferAsync(offer.Id.Value, CancellationToken.None)
            .ConfigureAwait(true);

        blockedDelete.Success.Should().BeFalse();
        blockedDelete.ErrorCode.Should().Be("offer_has_products");

        (
            await service
                .DeleteProductAsync(product.Id!.Value, CancellationToken.None)
                .ConfigureAwait(true)
        )
            .Success.Should()
            .BeTrue();

        (
            await service
                .DeleteOfferAsync(offer.Id.Value, CancellationToken.None)
                .ConfigureAwait(true)
        )
            .Success.Should()
            .BeTrue();
    }

    [Fact]
    public async Task UpdatePage_UnknownId_ReturnsNotFound()
    {
        DbContextOptions<VortexDbContext> options = NewOptions();
        CatalogAdminService service = NewService(options, out _);

        CatalogAdminResult result = await service
            .UpdatePageAsync(
                999,
                new CatalogPageUpdateSpec(
                    null,
                    "whatever",
                    null,
                    0,
                    CatalogPageLayout.Default3x3,
                    null,
                    null,
                    0,
                    true
                ),
                CancellationToken.None
            )
            .ConfigureAwait(true);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("page_not_found");
    }

    private sealed class TestDbContextFactory(DbContextOptions<VortexDbContext> options)
        : IDbContextFactory<VortexDbContext>
    {
        public VortexDbContext CreateDbContext() => new(options);
    }

    private sealed class FakeSnapshotProvider<TTag>(CatalogType catalogType)
        : ICatalogSnapshotProvider<TTag>
        where TTag : ICatalogTag
    {
        public int ReloadCount { get; private set; }

        public CatalogType CatalogType => catalogType;

        public CatalogSnapshot Current => CatalogSnapshot.Empty;

        public Task<CatalogSnapshot> GetSnapshotAsync(CancellationToken ct) =>
            Task.FromResult(CatalogSnapshot.Empty);

        public Task ReloadAsync(CancellationToken ct)
        {
            ReloadCount++;
            return Task.CompletedTask;
        }
    }
}
