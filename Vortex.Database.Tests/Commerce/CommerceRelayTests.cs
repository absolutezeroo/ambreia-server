using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vortex.Database.Commerce;
using Vortex.Database.Configuration;
using Vortex.Database.Context;
using Vortex.Primitives.Commerce;
using Vortex.Primitives.Events;
using Vortex.Primitives.Observability;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Database.Tests.Commerce;

/// <summary>
/// A purchase used to publish its event immediately after succeeding, outside any transaction, so a
/// crash between the commit and the publish lost it — and with it the quest progress and the daily
/// task that read it. The event is written with the operation's terminal transition now, and relayed
/// afterwards.
/// <para>
/// Which makes delivery at-least-once. These tests cover both halves: that a lost publish is
/// eventually made good, and that a consumer seeing the same event twice does not act on it twice.
/// </para>
/// </summary>
public sealed class CommerceRelayTests : IAsyncLifetime
{
    private const int PLAYER = 12;

    private SqliteConnection _conn = null!;
    private CommerceJournal _journal = null!;
    private readonly List<IEvent> _published = [];

    public async Task InitializeAsync()
    {
        _conn = new SqliteConnection("Filename=:memory:");
        await _conn.OpenAsync();

        DbContextOptions<VortexDbContext> options = new DbContextOptionsBuilder<VortexDbContext>()
            .UseSqlite(_conn)
            .Options;

        await using (VortexDbContext db = new(options))
        {
            await db.Database.EnsureCreatedAsync();
        }

        _journal = new CommerceJournal(
            new TestDbContextFactory(options),
            FakeProxy.Create<IVortexMetrics>(_ => null),
            NullLogger<CommerceJournal>.Instance
        );
    }

    public async Task DisposeAsync() => await _conn.DisposeAsync();

    /// <summary>
    /// The crash the outbox exists for: the operation completes, the publish never happens, and the
    /// sweep publishes it afterwards — the same event, rebuilt from what the transition stored.
    /// </summary>
    [Fact]
    public async Task AnEventThatWasNeverPublished_IsRelayedBySweep()
    {
        CommerceOperationId id = await CompletedPurchaseAsync(offerId: 77, quantity: 3);

        using CommerceRelayService relay = BuildRelay();
        await relay.RelayAsync(CancellationToken.None);

        _published
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<CatalogPurchasedEvent>()
            .Which.Should()
            .BeEquivalentTo(new CatalogPurchasedEvent(PLAYER, "Normal", 77, 3, 30, id.ToString()));
    }

    /// <summary>Relayed once. A sweep that republished on every tick would be worse than the loss.</summary>
    [Fact]
    public async Task AnEventAlreadyRelayed_IsNotRelayedAgain()
    {
        await CompletedPurchaseAsync(offerId: 78, quantity: 1);

        using CommerceRelayService relay = BuildRelay();

        await relay.RelayAsync(CancellationToken.None);
        await relay.RelayAsync(CancellationToken.None);

        _published.Should().ContainSingle();
    }

    /// <summary>
    /// An operation past its pivot for longer than the threshold is escalated and logged at critical.
    /// Nothing here repairs it — resuming a half-delivered purchase needs per-flow knowledge the
    /// sweep does not have — but it stops the operation sitting there with nobody knowing, which is
    /// what every commerce failure did before.
    /// </summary>
    [Fact]
    public async Task AnOperationStuckPastItsPivot_IsEscalated()
    {
        CommerceOperationId id = CommerceOperationId.New();
        await _journal.OpenAsync(
            id,
            CommerceOperationKind.MarketplaceBuy,
            PLAYER,
            "offer 5",
            CancellationToken.None
        );
        await _journal.TransitionAsync(
            id,
            CommerceOperationState.Pivoted,
            CommerceStepKeys.MARKETPLACE_DELIVER,
            null,
            CancellationToken.None
        );

        // Escalation is measured from the pivot, so the threshold has to be crossed rather than
        // waited out: zero minutes means "anything already pivoted".
        using CommerceRelayService relay = BuildRelay(stuckAfterMinutes: 0);
        await relay.EscalateAsync(CancellationToken.None);

        IReadOnlyList<CommerceOperationRecord> stuck = await _journal.GetIncompletePivotedAsync(
            10,
            CancellationToken.None
        );

        stuck.Single(r => r.Id == id).State.Should().Be(CommerceOperationState.NeedsIntervention);
    }

    [Fact]
    public async Task AnOperationJustPastItsPivot_IsLeftAlone()
    {
        CommerceOperationId id = CommerceOperationId.New();
        await _journal.OpenAsync(
            id,
            CommerceOperationKind.CatalogPurchase,
            PLAYER,
            null,
            CancellationToken.None
        );
        await _journal.TransitionAsync(
            id,
            CommerceOperationState.Pivoted,
            null,
            null,
            CancellationToken.None
        );

        using CommerceRelayService relay = BuildRelay(stuckAfterMinutes: 10);
        await relay.EscalateAsync(CancellationToken.None);

        IReadOnlyList<CommerceOperationRecord> stuck = await _journal.GetIncompletePivotedAsync(
            10,
            CancellationToken.None
        );

        stuck.Single(r => r.Id == id).State.Should().Be(CommerceOperationState.Pivoted);
    }

    /// <summary>
    /// The consumer half. Quest progress and the daily task both read the same purchase event, and a
    /// redelivery must advance neither of them a second time — while still letting a genuine second
    /// purchase through, and letting both consumers act on the first one.
    /// </summary>
    [Fact]
    public async Task ARedeliveredEvent_AdvancesEachConsumerOnce()
    {
        CommerceOperationId first = await CompletedPurchaseAsync(offerId: 80, quantity: 1);
        CommerceOperationId second = await CompletedPurchaseAsync(offerId: 80, quantity: 1);

        async Task<bool> QuestAsync(CommerceOperationId id) =>
            await CommerceReplayGuard.FirstDeliveryAsync(
                _journal,
                id.ToString(),
                "quest",
                CancellationToken.None
            );

        async Task<bool> DailyAsync(CommerceOperationId id) =>
            await CommerceReplayGuard.FirstDeliveryAsync(
                _journal,
                id.ToString(),
                "daily-task",
                CancellationToken.None
            );

        (await QuestAsync(first)).Should().BeTrue();
        (await DailyAsync(first)).Should().BeTrue("two consumers of one event are two deliveries");

        (await QuestAsync(first)).Should().BeFalse("this is the same purchase arriving again");
        (await DailyAsync(first)).Should().BeFalse();

        (await QuestAsync(second)).Should().BeTrue("and this is a different purchase");
    }

    /// <summary>An event raised outside a commerce operation is never replayed, so it always passes.</summary>
    [Fact]
    public async Task AnEventWithNoOperation_IsAlwaysDelivered()
    {
        for (int i = 0; i < 3; i++)
        {
            (
                await CommerceReplayGuard.FirstDeliveryAsync(
                    _journal,
                    string.Empty,
                    "quest",
                    CancellationToken.None
                )
            )
                .Should()
                .BeTrue();
        }
    }

    private async Task<CommerceOperationId> CompletedPurchaseAsync(int offerId, int quantity)
    {
        CommerceOperationId id = CommerceOperationId.New();

        await _journal.OpenAsync(
            id,
            CommerceOperationKind.CatalogPurchase,
            PLAYER,
            $"offer={offerId}",
            CancellationToken.None
        );

        await _journal.CompleteWithRelayAsync(
            id,
            new CatalogPurchasedEvent(PLAYER, "Normal", offerId, quantity, 30, id.ToString()),
            CancellationToken.None
        );

        return id;
    }

    /// <summary>
    /// The sweep used to ask for everything incomplete — interventions included — and skip them
    /// inside its loop, after the batch limit had been applied. A backlog of old interventions
    /// therefore filled the batch and nothing newer was ever escalated again. A hundred of them is
    /// the default; one is enough to show it.
    /// </summary>
    [Fact]
    public async Task ABacklogOfInterventions_DoesNotStarveANewerStuckOperation()
    {
        CommerceOperationId old = CommerceOperationId.New();
        await _journal.OpenAsync(
            old,
            CommerceOperationKind.CatalogPurchase,
            PLAYER,
            "the backlog",
            CancellationToken.None
        );
        await _journal.TransitionAsync(
            old,
            CommerceOperationState.Pivoted,
            null,
            null,
            CancellationToken.None
        );

        // One pass turns it into the intervention that used to fill the batch for ever after.
        using (CommerceRelayService first = BuildRelay(stuckAfterMinutes: 0, batchSize: 1))
        {
            await first.EscalateAsync(CancellationToken.None);
        }

        CommerceOperationId fresh = CommerceOperationId.New();
        await _journal.OpenAsync(
            fresh,
            CommerceOperationKind.CatalogPurchase,
            PLAYER,
            "stuck behind it",
            CancellationToken.None
        );
        await _journal.TransitionAsync(
            fresh,
            CommerceOperationState.Pivoted,
            null,
            null,
            CancellationToken.None
        );

        using CommerceRelayService relay = BuildRelay(stuckAfterMinutes: 0, batchSize: 1);
        await relay.EscalateAsync(CancellationToken.None);

        IReadOnlyList<CommerceOperationRecord> all = await _journal.GetIncompletePivotedAsync(
            10,
            CancellationToken.None
        );

        all.Single(r => r.Id == fresh)
            .State.Should()
            .Be(
                CommerceOperationState.NeedsIntervention,
                "an operation already escalated must not consume the batch a newer one needs"
            );
    }

    private CommerceRelayService BuildRelay(int stuckAfterMinutes = 10, int batchSize = 50) =>
        new(
            _journal,
            FakeProxy.Create<IEventPublisher>(call =>
            {
                _published.Add((IEvent)call.Args![0]!);

                return Task.CompletedTask;
            }),
            Options.Create(
                new CommerceRecoveryConfig
                {
                    RelayBatchSize = batchSize,
                    StuckAfterMinutes = stuckAfterMinutes,
                }
            ),
            NullLogger<CommerceRelayService>.Instance
        );

    private sealed class TestDbContextFactory(DbContextOptions<VortexDbContext> options)
        : IDbContextFactory<VortexDbContext>
    {
        public VortexDbContext CreateDbContext() => new(options);
    }
}
