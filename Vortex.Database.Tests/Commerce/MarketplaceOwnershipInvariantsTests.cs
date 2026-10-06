using System;
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
using Vortex.Database.Tests.Support;
using Vortex.Marketplace.Grains;
using Vortex.Primitives.Events;
using Vortex.Primitives.Inventory.Grains;
using Vortex.Primitives.Inventory.Snapshots;
using Vortex.Primitives.Marketplace.Providers;
using Vortex.Primitives.Marketplace.Snapshots;
using Vortex.Primitives.Observability;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Database.Tests.Commerce;

/// <summary>
/// Where the item actually is once it has been listed.
/// </summary>
/// <remarks>
/// The listing suite next door asserts that the inventory grain was <em>asked</em> to give the item
/// up — on a list filled by a fake standing in for the very component whose persistence is in
/// question. That assertion holds whether or not anything reaches the database, which is why the
/// duplication below survived it.
///
/// This asks the database instead, with the seller's furniture row really there. The predicate is
/// <c>InventoryFurnitureLoader</c>'s own, spelled out: player, no room, no wired chest, no jukebox.
/// Those columns are the whole definition of "in this player's inventory", and re-reading them is
/// exactly what happens when the grain is collected after two idle minutes or the player reconnects.
///
/// A row that still answers them after a listing is an item the seller gets back for free — and
/// every exit an offer has (sold, cancelled, expired) grants a fresh row through DeliverAsync, so
/// the original is a second copy however the listing ends.
/// </remarks>
public sealed class MarketplaceOwnershipInvariantsTests : IAsyncLifetime
{
    private const int SELLER = 4;
    private const int DEFINITION_ID = 42;
    private const int PRICE = 120;
    private const int ITEM_ID = 901;

    private SqliteConnection _conn = null!;
    private DbContextOptions<VortexDbContext> _options = null!;

    /// <summary>
    /// SQLite, since the pivot this suite is about claims the row with an ExecuteUpdate and the
    /// in-memory provider cannot run one. See <see cref="SqliteTestDb" />.
    /// </summary>
    public async Task InitializeAsync() => (_conn, _options) = await SqliteTestDb.OpenAsync();

    public async Task DisposeAsync() => await _conn.DisposeAsync();

    /// <summary>How many rows the seller's next inventory load would return.</summary>
    private async Task<int> ItemsTheInventoryWouldLoadAsync()
    {
        await using VortexDbContext db = new(_options);

        return await db.Furnitures.CountAsync(f =>
            f.PlayerEntityId == SELLER && f.RoomEntityId == null
        );
    }

    private Task SeedTheSellersItemAsync() =>
        SqliteTestDb.SeedFurnitureAsync(_options, ITEM_ID, SELLER, DEFINITION_ID);

    [Fact]
    public async Task AListedItem_HasLeftTheSellersInventory()
    {
        await SeedTheSellersItemAsync();

        (await ItemsTheInventoryWouldLoadAsync()).Should().Be(1, "it is the seller's to list");

        (int result, int _) = await BuildGrain()
            .MakeOfferAsync(ITEM_ID, PRICE, CancellationToken.None);

        result.Should().Be(0, "the listing succeeded");

        (await ItemsTheInventoryWouldLoadAsync())
            .Should()
            .Be(
                0,
                "the offer holds the item now -- a row still answering the inventory's predicate "
                    + "comes back on the next reload, and every exit the offer has grants a fresh one"
            );
    }

    /// <summary>
    /// The other direction, and the reason the durable half goes after the offer row rather than
    /// before it: a listing that cannot complete must leave the seller exactly as it found them.
    /// </summary>
    [Fact]
    public async Task AListingThatDoesNotComplete_LeavesTheItemWithTheSeller()
    {
        await SeedTheSellersItemAsync();

        (int result, int _) = await BuildGrain(removeReturnsFalse: true)
            .MakeOfferAsync(ITEM_ID, PRICE, CancellationToken.None);

        result.Should().Be(1, "the listing was abandoned");

        (await ItemsTheInventoryWouldLoadAsync()).Should().Be(1, "so nothing left the seller");
    }

    private MarketplacePurchaseGrain BuildGrain(bool removeReturnsFalse = false)
    {
        TestDbContextFactory factory = new(_options);

        MarketplacePurchaseGrain grain = new(
            factory,
            BuildGrainFactory(removeReturnsFalse),
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

    /// <summary>
    /// The inventory grain is faked exactly as far as it is honest to fake it: its furniture cache is
    /// in-memory state that a unit test has no way to stand up, and the real
    /// <c>RemoveFurnitureAsync</c> writes nothing to the database either, so the fake and the real
    /// one agree on everything this test measures.
    /// </summary>
    private IGrainFactory BuildGrainFactory(bool removeReturnsFalse)
    {
        IInventoryGrain inventory = FakeProxy.Create<IInventoryGrain>(call =>
            call.Method.Name switch
            {
                nameof(IInventoryGrain.GetItemSnapshotAsync) =>
                    Task.FromResult<FurnitureItemSnapshot?>(
                        MarketplaceItems.Snapshot(ITEM_ID, DEFINITION_ID, SELLER)
                    ),
                nameof(IInventoryGrain.RemoveFurnitureAsync) => Task.FromResult(
                    !removeReturnsFalse
                ),
                _ => null,
            }
        );

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
