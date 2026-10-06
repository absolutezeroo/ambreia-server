using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans;
using Vortex.Database.Commerce;
using Vortex.Database.Context;
using Vortex.Database.Entities.Commerce;
using Vortex.Players.Configuration;
using Vortex.Players.Grains;
using Vortex.Primitives.Commerce;
using Vortex.Primitives.Events;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Permissions;
using Vortex.Primitives.Players.Enums;
using Vortex.Primitives.Players.Enums.Wallet;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Players.Providers;
using Vortex.Primitives.Players.Wallet;
using Vortex.Progression.Achievements;
using Vortex.Progression.Configuration;
using Vortex.Progression.Grains;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Wallet;

/// <summary>
/// Buying club months debits and then applies the months -- writing subscription rows, granting
/// badges, raising events. Any of that can throw, and when it did the credits were already gone with
/// no membership to show for them.
/// </summary>
public sealed class ClubPurchaseRefundTests
{
    private const int PLAYER_ID = 42;
    private const int COST = 75;

    [Fact]
    public async Task ApplyingTheMonthsFails_RefundsTheCredits()
    {
        Harness harness = new Harness();

        Func<Task> act = () =>
            harness.Grain.PurchaseClubAsync(1, isVip: false, COST, CancellationToken.None);

        // The player row is missing, which is exactly what ApplyClubMonthsAsync throws on.
        await act.Should().ThrowAsync<Exception>().ConfigureAwait(true);

        harness.Wallet.DebitedAmounts.Should().Equal(COST);
        harness.Wallet.RefundedAmounts.Should().Equal(COST);
    }

    /// <summary>
    /// A purchase that had to be unwound is not left claiming it went through.
    /// </summary>
    /// <remarks>
    /// The club was the tenth flow moving value with no operation id -- one the audit did not list.
    /// The state is what makes the journal useful: FailedBeforePivot says the credits went back,
    /// where an operation stuck at Debited or Pivoted is one somebody has to chase.
    /// </remarks>
    [Fact]
    public async Task ApplyingTheMonthsFails_RecordsTheOperationAsCompensated()
    {
        Harness harness = new Harness();

        Func<Task> act = () =>
            harness.Grain.PurchaseClubAsync(1, isVip: false, COST, CancellationToken.None);

        await act.Should().ThrowAsync<Exception>().ConfigureAwait(true);

        await using VortexDbContext db = harness.NewDbContext();
        CommerceOperationEntity operation = await db.CommerceOperations.SingleAsync();

        operation.Kind.Should().Be(CommerceOperationKind.ClubPurchase);
        operation.State.Should().Be(CommerceOperationState.FailedBeforePivot);
    }

    [Fact]
    public async Task NotEnoughCredits_NeverTouchesTheSubscription()
    {
        Harness harness = new Harness();
        harness.Wallet.DebitSucceeds = false;

        ClubPurchaseResult result = await harness
            .Grain.PurchaseClubAsync(1, isVip: false, COST, CancellationToken.None)
            .ConfigureAwait(true);

        result.Should().Be(ClubPurchaseResult.NotEnoughCredits);
        harness.Wallet.RefundedAmounts.Should().BeEmpty();
    }

    [Fact]
    public async Task ZeroMonths_IsANoOpAndCostsNothing()
    {
        Harness harness = new Harness();

        ClubPurchaseResult result = await harness
            .Grain.PurchaseClubAsync(0, isVip: false, COST, CancellationToken.None)
            .ConfigureAwait(true);

        result.Should().Be(ClubPurchaseResult.Success);
        harness.Wallet.DebitedAmounts.Should().BeEmpty();
    }

    private sealed class Harness
    {
        public Harness()
        {
            DbContextOptions<VortexDbContext> options =
                new DbContextOptionsBuilder<VortexDbContext>()
                    .UseInMemoryDatabase($"club-refund-{Guid.NewGuid()}")
                    .Options;

            Grain = GrainActivationContext.CreateWithIntegerKey<PlayerGrain>(
                PLAYER_ID,
                new TestDbContextFactory(options),
                BuildGrainFactory(),
                FakeProxy.Create<IEventPublisher>(_ => null),
                NullLogger<PlayerGrain>.Instance,
                Options.Create(new ClubConfig()),
                // A real journal over the same database: whether the months were paid for is the
                // question here, and that is now recorded rather than merely returned.
                Journal = new CommerceJournal(
                    new TestDbContextFactory(options),
                    FakeProxy.Create<IVortexMetrics>(_ => null),
                    NullLogger<CommerceJournal>.Instance
                ),
                // No roles: the grain resolves staff-ness once at load and nothing here cares what
                // the answer is.
                FakeProxy.Create<IPermissionService>(_ => Task.FromResult(PermissionSet.Empty))
            );

            DbOptions = options;
        }

        public PlayerGrain Grain { get; }

        public CommerceJournal Journal { get; }

        public DbContextOptions<VortexDbContext> DbOptions { get; }

        public VortexDbContext NewDbContext() => new(DbOptions);

        public RecordingWallet Wallet { get; } = new();

        private IGrainFactory BuildGrainFactory() =>
            FakeProxy.Create<IGrainFactory>(call =>
                call.Method.IsGenericMethod
                && call.Method.GetGenericArguments()[0] == typeof(IPlayerWalletGrain)
                    ? Wallet.AsGrain()
                    : null
            );
    }

    /// <summary>Records what was taken and what was given back -- the two numbers these tests are
    /// about.</summary>
    public sealed class RecordingWallet
    {
        public bool DebitSucceeds { get; set; } = true;

        public List<int> DebitedAmounts { get; } = [];

        public List<int> RefundedAmounts { get; } = [];

        public IPlayerWalletGrain AsGrain() =>
            FakeProxy.Create<IPlayerWalletGrain>(call =>
            {
                switch (call.Method.Name)
                {
                    case nameof(IPlayerWalletGrain.TryDebitAsync):
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
                                        Amount = COST,
                                    }
                                )
                            );
                        }

                        Record(DebitedAmounts, call.Args![0]!);

                        return Task.FromResult(WalletDebitResult.Success());

                    case nameof(IPlayerWalletGrain.CreditBackAsync):
                        Record(RefundedAmounts, call.Args![0]!);

                        return Task.CompletedTask;

                    default:
                        return null;
                }
            });

        private static void Record(List<int> sink, object requests)
        {
            foreach (WalletDebitRequest request in (List<WalletDebitRequest>)requests)
            {
                sink.Add(request.Amount);
            }
        }
    }

    private sealed class TestDbContextFactory(DbContextOptions<VortexDbContext> options)
        : IDbContextFactory<VortexDbContext>
    {
        public VortexDbContext CreateDbContext() => new VortexDbContext(options);
    }
}
