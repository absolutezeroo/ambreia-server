using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Vortex.Dashboard.API.Admin.Catalogue;
using Vortex.Database.Context;
using Vortex.Database.Entities.Catalog;
using Vortex.Database.Entities.Furniture;
using Vortex.Primitives.Catalog;
using Vortex.Primitives.Catalog.Enums;
using Vortex.Primitives.Furniture.Admin;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Furniture.Snapshots;
using Vortex.Primitives.Furniture.StuffData;
using Vortex.Primitives.Rooms.Enums;
using Xunit;

namespace Vortex.Dashboard.Tests;

/// <summary>
/// Covers the invariants <c>FurnitureAdminService</c> is responsible for: the (sprite_id,
/// product_type, category) uniqueness the DB index enforces, the delete-blocked guards that
/// prevent orphaning placed/owned furniture or breaking a catalog product, and that a successful
/// write reloads the live <see cref="IFurnitureDefinitionProvider"/> snapshot -- the same
/// "DB write not reflected in live state" bug class the catalog admin tests cover.
/// </summary>
public sealed class FurnitureAdminServiceTests
{
    private static DbContextOptions<VortexDbContext> NewOptions() =>
        new DbContextOptionsBuilder<VortexDbContext>()
            .UseInMemoryDatabase($"furniture-admin-{Guid.NewGuid():N}")
            .Options;

    private static FurnitureDefinitionUpsertSpec NewSpec(int spriteId, string name) =>
        new(
            spriteId,
            name,
            ProductType.Floor,
            FurnitureCategory.Default,
            "none",
            0,
            1,
            1,
            0.0,
            true,
            false,
            false,
            false,
            false,
            true,
            true,
            true,
            FurnitureUsageType.Controller,
            null,
            StuffDataType.LegacyKey
        );

    private static FurnitureAdminService NewService(
        DbContextOptions<VortexDbContext> options,
        out FakeDefinitionProvider provider
    )
    {
        provider = new FakeDefinitionProvider();

        return new FurnitureAdminService(
            new TestDbContextFactory(options),
            provider,
            NullLogger<FurnitureAdminService>.Instance
        );
    }

    [Fact]
    public async Task CreateAsync_Succeeds_AndReloadsLiveProvider()
    {
        DbContextOptions<VortexDbContext> options = NewOptions();
        FurnitureAdminService service = NewService(options, out FakeDefinitionProvider provider);

        FurnitureAdminResult result = await service
            .CreateAsync(NewSpec(100, "throne"), CancellationToken.None)
            .ConfigureAwait(true);

        result.Success.Should().BeTrue();
        result.Id.Should().NotBeNull();

        await using VortexDbContext verify = new(options);
        (await verify.FurnitureDefinitions.FindAsync(result.Id!.Value).ConfigureAwait(true))
            .Should()
            .NotBeNull();
        provider.ReloadCount.Should().Be(1);
    }

    [Fact]
    public async Task CreateAsync_DuplicateSpriteTypeCategory_IsRejected()
    {
        DbContextOptions<VortexDbContext> options = NewOptions();
        FurnitureAdminService service = NewService(options, out _);

        await service
            .CreateAsync(NewSpec(200, "chair_a"), CancellationToken.None)
            .ConfigureAwait(true);
        FurnitureAdminResult dup = await service
            .CreateAsync(NewSpec(200, "chair_b"), CancellationToken.None)
            .ConfigureAwait(true);

        dup.Success.Should().BeFalse();
        dup.ErrorCode.Should().Be("duplicate_sprite_type_category");
    }

    [Fact]
    public async Task UpdateAsync_UnknownId_ReturnsNotFound()
    {
        DbContextOptions<VortexDbContext> options = NewOptions();
        FurnitureAdminService service = NewService(options, out _);

        FurnitureAdminResult result = await service
            .UpdateAsync(999, NewSpec(1, "x"), CancellationToken.None)
            .ConfigureAwait(true);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("definition_not_found");
    }

    [Fact]
    public async Task DeleteAsync_WithPlacedInstance_IsBlocked()
    {
        DbContextOptions<VortexDbContext> options = NewOptions();
        FurnitureAdminService service = NewService(options, out _);

        FurnitureAdminResult created = await service
            .CreateAsync(NewSpec(300, "lamp"), CancellationToken.None)
            .ConfigureAwait(true);

        await using (VortexDbContext db = new(options))
        {
            db.Furnitures.Add(
                new FurnitureEntity { FurnitureDefinitionEntityId = created.Id!.Value }
            );
            await db.SaveChangesAsync().ConfigureAwait(true);
        }

        FurnitureAdminResult delete = await service
            .DeleteAsync(created.Id!.Value, CancellationToken.None)
            .ConfigureAwait(true);

        delete.Success.Should().BeFalse();
        delete.ErrorCode.Should().Be("definition_has_instances");
    }

    [Fact]
    public async Task DeleteAsync_UsedByCatalogProduct_IsBlocked()
    {
        DbContextOptions<VortexDbContext> options = NewOptions();
        FurnitureAdminService service = NewService(options, out _);

        FurnitureAdminResult created = await service
            .CreateAsync(NewSpec(400, "table"), CancellationToken.None)
            .ConfigureAwait(true);

        await using (VortexDbContext db = new(options))
        {
            CatalogPageEntity page = new()
            {
                CatalogType = CatalogType.Normal,
                Localization = "shop",
                Icon = 0,
                Layout = CatalogPageLayout.Default3x3,
                SortOrder = 0,
                Visible = true,
            };
            db.CatalogPages.Add(page);
            await db.SaveChangesAsync().ConfigureAwait(true);

            CatalogOfferEntity offer = new()
            {
                CatalogPageEntityId = page.Id,
                Page = page,
                LocalizationId = "table_offer",
                CostCredits = 10,
                CostCurrency = 0,
                CanGift = true,
                CanBundle = true,
                ClubLevel = 0,
                Visible = true,
            };
            db.CatalogOffers.Add(offer);
            await db.SaveChangesAsync().ConfigureAwait(true);

            db.CatalogProducts.Add(
                new CatalogProductEntity
                {
                    CatalogOfferEntityId = offer.Id,
                    Offer = offer,
                    ProductType = ProductType.Floor,
                    FurnitureDefinitionEntityId = created.Id,
                    Quantity = 1,
                    UniqueSize = 0,
                    UniqueRemaining = 0,
                }
            );
            await db.SaveChangesAsync().ConfigureAwait(true);
        }

        FurnitureAdminResult delete = await service
            .DeleteAsync(created.Id!.Value, CancellationToken.None)
            .ConfigureAwait(true);

        delete.Success.Should().BeFalse();
        delete.ErrorCode.Should().Be("definition_used_by_catalog_product");
    }

    [Fact]
    public async Task DeleteAsync_Unused_Succeeds()
    {
        DbContextOptions<VortexDbContext> options = NewOptions();
        FurnitureAdminService service = NewService(options, out FakeDefinitionProvider provider);

        FurnitureAdminResult created = await service
            .CreateAsync(NewSpec(500, "plant"), CancellationToken.None)
            .ConfigureAwait(true);

        FurnitureAdminResult delete = await service
            .DeleteAsync(created.Id!.Value, CancellationToken.None)
            .ConfigureAwait(true);

        delete.Success.Should().BeTrue();
        provider.ReloadCount.Should().Be(2); // once for create, once for delete
    }

    private sealed class TestDbContextFactory(DbContextOptions<VortexDbContext> options)
        : IDbContextFactory<VortexDbContext>
    {
        public VortexDbContext CreateDbContext() => new(options);
    }

    private sealed class FakeDefinitionProvider : IFurnitureDefinitionProvider
    {
        public int ReloadCount { get; private set; }

        public FurnitureDefinitionSnapshot? TryGetDefinition(int id) => null;

        public FurnitureDefinitionSnapshot? TryGetDefinitionByName(string name) => null;

        public Task ReloadAsync(CancellationToken ct)
        {
            ReloadCount++;
            return Task.CompletedTask;
        }
    }
}
