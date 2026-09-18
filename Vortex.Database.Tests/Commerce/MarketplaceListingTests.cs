using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans;
using Vortex.Database.Commerce;
using Vortex.Database.Context;
using Vortex.Database.Entities.Furniture;
using Vortex.Database.Entities.Marketplace;
using Vortex.Database.Tests.Support;
using Vortex.Marketplace.Grains;
using Vortex.Primitives.Events;
using Vortex.Primitives.Inventory.Grains;
using Vortex.Primitives.Inventory.Snapshots;
using Vortex.Primitives.Marketplace.Providers;
using Vortex.Primitives.Marketplace.Snapshots;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Rooms.Object;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Database.Tests.Commerce;

/// <summary>
/// Listing an item on the marketplace, and what is left behind when it does not go through.
/// </summary>
/// <remarks>
/// This suite used to be the one exception on the in-memory provider, because listing inserts an
/// offer through EF and SQLite refuses to insert a VortexEntity at all — created_at is
/// identity-generated, updated_at computed, and EnsureCreated gives neither a default. The note
/// ended "nothing here needs ExecuteUpdate", and that is no longer true: the pivot claims the row
/// rather than reading it and writing it after, so the provider has to be able to run the claim.
/// <see cref="SqliteTestDb" /> supplies the two column defaults MySQL's migrations already carry.
/// </remarks>
public sealed class MarketplaceListingTests : IAsyncLifetime
{
    private const int SELLER = 4;
    private const int DEFINITION_ID = 42;
    private const int PRICE = 120;
    private const int ITEM_ID = 901;

    private SqliteConnection _conn = null!;
    private DbContextOptions<VortexDbContext> _options = null!;

    private readonly List<int> _removed = [];

    private bool _removeReturnsFalse;

    public async Task InitializeAsync() => (_conn, _options) = await SqliteTestDb.OpenAsync();

    public async Task DisposeAsync() => await _conn.DisposeAsync();

    /// <summary>
    /// The seller's actual row. Listing takes it for good now, so there has to be one to take —
    /// before, this suite listed an item that existed only as a fake grain's return value.
    /// </summary>
    private Task SeedTheSellersItemAsync() =>
        SqliteTestDb.SeedFurnitureAsync(_options, ITEM_ID, SELLER, DEFINITION_ID);

    [Fact]
    public async Task ACompleteListing_TakesTheItemAndPublishesTheOffer()
    {
        await SeedTheSellersItemAsync();

        (int result, int offerId) = await BuildGrain()
            .MakeOfferAsync(ITEM_ID, PRICE, CancellationToken.None);

        result.Should().Be(0);
        _removed.Should().Equal([ITEM_ID]);

        await using VortexDbContext db = new(_options);
        MarketplaceOfferEntity offer = await db.MarketplaceOffers.SingleAsync(o => o.Id == offerId);

        offer.State.Should().Be(MarketplaceOfferState.Active, "and only then is it on the market");
    }

    /// <summary>
    /// The offer is written before the item is taken, so a withdrawal that does not go through leaves
    /// an offer nobody could ever buy. It is removed rather than left there: as far as the seller is
    /// concerned this listing never happened.
    /// </summary>
    /// <remarks>
    /// The other order — take the item, then write the offer — destroyed the furniture outright when
    /// anything went wrong in between: gone from the inventory and held by nothing.
    /// </remarks>
    [Fact]
    public async Task AWithdrawalThatFails_LeavesNoOfferAtAll()
    {
        _removeReturnsFalse = true;

        (int result, int _) = await BuildGrain()
            .MakeOfferAsync(ITEM_ID, PRICE, CancellationToken.None);

        result.Should().Be(1);
        _removed.Should().BeEmpty();

        await using VortexDbContext db = new(_options);
        (await db.MarketplaceOffers.CountAsync()).Should().Be(0);
    }

    private MarketplacePurchaseGrain BuildGrain()
    {
        TestDbContextFactory factory = new(_options);

        MarketplacePurchaseGrain grain = new(
            factory,
            BuildGrainFactory(),
            FakeProxy.Create<IMarketplaceSettingsProvider>(call =>
                call.Method.Name == nameof(IMarketplaceSettingsProvider.GetSettings)
                    ? new MarketplaceSettingsSnapshot
                    {
                        CommissionPercent = 1,
                        OfferDurationSeconds = 3600,
                        AveragePricePeriodDays = 7,
                        HalfTaxLimit = 1_000_000_000,
                    }
                    : null
            ),
            FakeProxy.Create<IEventPublisher>(_ => Task.CompletedTask),
            new CommerceJournal(
                factory,
                FakeProxy.Create<IVortexMetrics>(_ => null),
                NullLogger<CommerceJournal>.Instance
            ),
            NullLogger<MarketplacePurchaseGrain>.Instance
        );

        GrainContexts.Install(grain, "marketplacepurchase", SELLER);

        return grain;
    }

    private IGrainFactory BuildGrainFactory()
    {
        IInventoryGrain inventory = FakeProxy.Create<IInventoryGrain>(call =>
        {
            switch (call.Method.Name)
            {
                case nameof(IInventoryGrain.GetItemSnapshotAsync):
                    return Task.FromResult<FurnitureItemSnapshot?>(
                        MarketplaceItems.Snapshot(ITEM_ID, DEFINITION_ID, SELLER)
                    );

                case nameof(IInventoryGrain.RemoveFurnitureAsync):
                    if (_removeReturnsFalse)
                    {
                        return Task.FromResult(false);
                    }

                    _removed.Add(((RoomObjectId)call.Args![0]!).Value);

                    return Task.FromResult(true);

                default:
                    return null;
            }
        });

        return FakeProxy.Create<IGrainFactory>(call =>
            call.Method.IsGenericMethod
            && call.Method.GetGenericArguments()[0] == typeof(IInventoryGrain)
                ? inventory
                : null
        );
    }

    private sealed class TestDbContextFactory(DbContextOptions<VortexDbContext> options)
        : IDbContextFactory<VortexDbContext>
    {
        public VortexDbContext CreateDbContext() => new(options);
    }
}
