using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans;
using Orleans.Runtime;
using Vortex.Database.Context;
using Vortex.Database.Entities.Marketplace;
using Vortex.Marketplace.Grains;
using Vortex.Primitives.Events;
using Vortex.Primitives.Inventory.Grains;
using Vortex.Primitives.Marketplace.Providers;
using Vortex.Primitives.Marketplace.Snapshots;
using Vortex.Primitives.Players.Enums.Wallet;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Players.Wallet;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Database.Tests.Marketplace;

/// <summary>
/// One offer, one item, and any number of people who can click Buy on it. The claim is an
/// ExecuteUpdate guarded on the offer still being Active, which is the only thing standing between
/// two buyers and one piece of furniture sold twice.
/// </summary>
/// <remarks>
/// On SQLite rather than the in-memory provider, because the in-memory provider does not implement
/// ExecuteUpdate at all -- it would throw, and a test that cannot run the guard cannot vouch for it.
/// </remarks>
public sealed class MarketplaceClaimRaceTests : IAsyncLifetime
{
    private const int SELLER = 1;
    private const int BUYER_A = 10;
    private const int BUYER_B = 20;
    private const int PRICE = 100;
    private const int OFFER_ID = 500;

    private SqliteConnection _conn = null!;
    private DbContextOptions<VortexDbContext> _options = null!;
    private readonly List<(int Player, int Amount)> _debits = [];
    private readonly List<(int Player, int Amount)> _refunds = [];
    private readonly List<int> _granted = [];

    private Func<Task>? _onDebit;
    private readonly HashSet<string> _deliveredSteps = [];

    private bool _grantThrows;

    public async Task InitializeAsync()
    {
        _conn = new SqliteConnection("Filename=:memory:");
        await _conn.OpenAsync();
        _options = new DbContextOptionsBuilder<VortexDbContext>().UseSqlite(_conn).Options;

        await using VortexDbContext db = new(_options);
        await db.Database.EnsureCreatedAsync();

        // The offer points at a seller and a furniture definition. Seeding both would mean seeding
        // most of the schema to test one UPDATE guard, and referential integrity is not what is
        // under test here -- the claim is.
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF");

        // Seeded in raw SQL: created_at is DatabaseGenerated(Identity) and updated_at is Computed,
        // so EF never writes either. MySQL fills them from column defaults the migrations declare;
        // EnsureCreated on SQLite makes the columns NOT NULL with no default, so an EF insert of any
        // VortexEntity fails there. Updates are unaffected, which is all these tests do afterwards.
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO marketplace_offers
              (id, seller_id, definition_id, sprite_id, furni_type, extra_data, price, state,
               credits_owed, expires_at, created_at, updated_at)
            VALUES
              ({0}, {1}, 42, 7, 1, '', {2}, {3}, 0, datetime('now', '+1 day'),
               datetime('now'), datetime('now'))
            """,
            OFFER_ID,
            SELLER,
            PRICE,
            (int)MarketplaceOfferState.Active
        );
    }

    public async Task DisposeAsync() => await _conn.DisposeAsync();

    private async Task<MarketplaceOfferEntity> OfferAsync()
    {
        await using VortexDbContext db = new(_options);

        return await db.MarketplaceOffers.AsNoTracking().SingleAsync(o => o.Id == OFFER_ID);
    }

    /// <summary>
    /// A second buyer arriving after the sale is turned away by the opening lookup and never
    /// reaches the claim, so the guard has to be exercised the only way it fires in production:
    /// both buyers read the offer as Active, and one of them commits first. The debit hook is the
    /// interleaving point -- the second purchase runs to completion while the first is between
    /// reading and claiming.
    /// </summary>
    [Fact]
    public async Task TwoBuyersInsideTheSameWindow_LeaveOneCharged()
    {
        MarketplacePurchaseGrain first = BuildGrain(BUYER_A);
        MarketplacePurchaseGrain second = BuildGrain(BUYER_B);

        _onDebit = async () =>
        {
            _onDebit = null;
            (await second.BuyOfferAsync(OFFER_ID, CancellationToken.None)).Should().Be(0);
        };

        int firstResult = await first.BuyOfferAsync(OFFER_ID, CancellationToken.None);

        // 1 is "no longer available", which the handler maps to the AS3 "not available" result.
        // Before this was caught, losing the race threw out of the grain and out of the handler,
        // and the buyer's client was never answered at all.
        firstResult.Should().Be(1, "the offer was claimed while this purchase was mid-flight");

        MarketplaceOfferEntity offer = await OfferAsync();
        offer.State.Should().Be(MarketplaceOfferState.Sold);

        // exactly one piece of furniture left the offer
        _granted.Should().Equal(42);

        // both were charged, and the loser got it back
        _debits.Should().BeEquivalentTo([(BUYER_A, PRICE), (BUYER_B, PRICE)]);
        _refunds.Should().Equal((BUYER_A, PRICE));
    }

    /// <summary>
    /// A claim that succeeds and then fails to deliver has to put the offer back, or the seller's
    /// furniture is Sold, undelivered and unbuyable.
    /// </summary>
    [Fact]
    public async Task AGrantThatFails_RelistsTheOfferAndRefunds()
    {
        _grantThrows = true;

        Func<Task> act = () => BuildGrain(BUYER_A).BuyOfferAsync(OFFER_ID, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();

        MarketplaceOfferEntity offer = await OfferAsync();
        offer.State.Should().Be(MarketplaceOfferState.Active);
        offer.CreditsOwed.Should().Be(0);
        _refunds.Should().Equal((BUYER_A, PRICE));
    }

    [Fact]
    public async Task TheSeller_IsOwedThePriceMinusCommission()
    {
        (await BuildGrain(BUYER_A).BuyOfferAsync(OFFER_ID, CancellationToken.None)).Should().Be(0);

        MarketplaceOfferEntity offer = await OfferAsync();

        // 1% of 100, floored at one credit by the grain
        offer.CreditsOwed.Should().Be(PRICE - 1);
        _refunds.Should().BeEmpty();
    }

    [Fact]
    public async Task AnExpiredOffer_IsRefusedBeforeAnyDebit()
    {
        await using (VortexDbContext db = new(_options))
        {
            await db
                .MarketplaceOffers.Where(o => o.Id == OFFER_ID)
                .ExecuteUpdateAsync(up =>
                    up.SetProperty(p => p.ExpiresAt, DateTime.UtcNow.AddDays(-1))
                );
        }

        (await BuildGrain(BUYER_A).BuyOfferAsync(OFFER_ID, CancellationToken.None)).Should().Be(1);

        _debits.Should().BeEmpty();
        _granted.Should().BeEmpty();
    }

    private MarketplacePurchaseGrain BuildGrain(int playerId)
    {
        MarketplacePurchaseGrain grain = new(
            new TestDbContextFactory(_options),
            BuildGrainFactory(playerId),
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
            new Commerce.NullCommerceJournal(),
            NullLogger<MarketplacePurchaseGrain>.Instance
        );

        GrainId grainId = GrainId.Create(
            GrainType.Create("marketplacepurchase"),
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

        return grain;
    }

    private IGrainFactory BuildGrainFactory(int playerId)
    {
        IPlayerWalletGrain wallet = FakeProxy.Create<IPlayerWalletGrain>(call =>
        {
            switch (call.Method.Name)
            {
                case nameof(IPlayerWalletGrain.TryDebitAsync):
                    _debits.Add((playerId, ((List<WalletDebitRequest>)call.Args![0]!)[0].Amount));

                    return RunDebitHookAsync();

                case nameof(IPlayerWalletGrain.CreditBackAsync):
                    _refunds.Add((playerId, ((List<WalletDebitRequest>)call.Args![0]!)[0].Amount));

                    return Task.CompletedTask;

                default:
                    return null;
            }
        });

        IInventoryGrain inventory = FakeProxy.Create<IInventoryGrain>(call =>
        {
            if (call.Method.Name != nameof(IInventoryGrain.GrantFurnitureDefinitionCopiesAsync))
            {
                return null;
            }

            if (_grantThrows)
            {
                throw new InvalidOperationException("inventory unreachable");
            }

            // The receipt-carrying overload: the real grain writes the receipt in the same commit as
            // the rows, so a step that has already delivered delivers nothing on a retry.
            if (!_deliveredSteps.Add($"{call.Args![3]}:{call.Args![4]}"))
            {
                return Task.CompletedTask;
            }

            for (int i = 0; i < (int)call.Args![2]!; i++)
            {
                _granted.Add((int)call.Args![0]!);
            }

            return Task.CompletedTask;
        });

        return FakeProxy.Create<IGrainFactory>(call =>
            call.Method.IsGenericMethod
                ? call.Method.GetGenericArguments()[0] switch
                {
                    Type t when t == typeof(IPlayerWalletGrain) => wallet,
                    Type t when t == typeof(IInventoryGrain) => inventory,
                    _ => null,
                }
                : null
        );
    }

    private async Task<WalletDebitResult> RunDebitHookAsync()
    {
        Func<Task>? hook = _onDebit;

        if (hook is not null)
        {
            await hook();
        }

        return WalletDebitResult.Success();
    }

    private sealed class TestDbContextFactory(DbContextOptions<VortexDbContext> options)
        : IDbContextFactory<VortexDbContext>
    {
        public VortexDbContext CreateDbContext() => new(options);
    }
}
