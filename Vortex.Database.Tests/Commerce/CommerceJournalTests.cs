using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Vortex.Database.Commerce;
using Vortex.Database.Context;
using Vortex.Primitives.Commerce;
using Vortex.Primitives.Observability;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Database.Tests.Commerce;

/// <summary>
/// The journal is what makes a post-pivot step safe to retry, so the thing worth testing is that a
/// step recorded twice applies once. Everything else in the commerce protocol rests on that.
/// </summary>
/// <remarks>
/// SQLite rather than the in-memory provider: the mechanism <em>is</em> a unique index, and the
/// in-memory provider does not enforce one. A test on in-memory would pass while proving nothing.
/// </remarks>
public sealed class CommerceJournalTests : IAsyncLifetime
{
    private const int PLAYER = 5;

    private SqliteConnection _conn = null!;
    private CommerceJournal _journal = null!;

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

    [Fact]
    public async Task AStepRecordedTwice_IsAppliedOnce()
    {
        CommerceOperationId id = CommerceOperationId.New();
        await _journal.OpenAsync(
            id,
            CommerceOperationKind.CatalogPurchase,
            PLAYER,
            null,
            CancellationToken.None
        );

        bool first = await _journal.TryRecordStepAsync(
            id,
            CommerceStepKeys.LOCAL_GRANT,
            "granted",
            CancellationToken.None
        );
        bool second = await _journal.TryRecordStepAsync(
            id,
            CommerceStepKeys.LOCAL_GRANT,
            "granted again",
            CancellationToken.None
        );

        first.Should().BeTrue();
        second.Should().BeFalse("the unique index is what tells a replay it already ran");

        (
            await _journal.GetStepResultAsync(
                id,
                CommerceStepKeys.LOCAL_GRANT,
                CancellationToken.None
            )
        )
            .Should()
            .Be(
                "granted",
                "a replay gets the earlier answer back, not the one it just tried to write"
            );
    }

    [Fact]
    public async Task TheSameStepInTwoOperations_RunsInBoth()
    {
        CommerceOperationId first = CommerceOperationId.New();
        CommerceOperationId second = CommerceOperationId.New();

        await _journal.OpenAsync(
            first,
            CommerceOperationKind.Gift,
            PLAYER,
            null,
            CancellationToken.None
        );
        await _journal.OpenAsync(
            second,
            CommerceOperationKind.Gift,
            PLAYER,
            null,
            CancellationToken.None
        );

        (
            await _journal.TryRecordStepAsync(
                first,
                CommerceStepKeys.GIFT_WRAP,
                null,
                CancellationToken.None
            )
        )
            .Should()
            .BeTrue();
        (
            await _journal.TryRecordStepAsync(
                second,
                CommerceStepKeys.GIFT_WRAP,
                null,
                CancellationToken.None
            )
        )
            .Should()
            .BeTrue("two purchases of the same gift are two operations, not a replay of one");
    }

    /// <summary>
    /// An offer that grants three effects is three steps, not one — otherwise the second and third
    /// would read the first one's receipt and be skipped.
    /// </summary>
    [Fact]
    public async Task IndexedSteps_AreDistinct()
    {
        CommerceOperationId id = CommerceOperationId.New();
        await _journal.OpenAsync(
            id,
            CommerceOperationKind.CatalogPurchase,
            PLAYER,
            null,
            CancellationToken.None
        );

        for (int i = 0; i < 3; i++)
        {
            (
                await _journal.TryRecordStepAsync(
                    id,
                    CommerceStepKeys.Indexed(CommerceStepKeys.EFFECT, i),
                    null,
                    CancellationToken.None
                )
            )
                .Should()
                .BeTrue();
        }
    }

    /// <summary>
    /// The pivot time is stamped once and never moves. "How long has this been stuck past its pivot"
    /// is the alert that matters, and a pivot time that re-stamped on every retry would reset it
    /// exactly when it should be growing.
    /// </summary>
    [Fact]
    public async Task ThePivotTime_IsStampedOnceAndNeverMoves()
    {
        CommerceOperationId id = CommerceOperationId.New();
        await _journal.OpenAsync(
            id,
            CommerceOperationKind.MarketplaceBuy,
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
        DateTime? first = (await PivotedAsync(id));

        await _journal.TransitionAsync(
            id,
            CommerceOperationState.Completing,
            CommerceStepKeys.MARKETPLACE_DELIVER,
            "inventory unreachable",
            CancellationToken.None
        );

        (await PivotedAsync(id)).Should().Be(first);
    }

    [Fact]
    public async Task AnOperationPastItsPivot_ShowsUpForRecoveryUntilItCompletes()
    {
        CommerceOperationId id = CommerceOperationId.New();
        await _journal.OpenAsync(
            id,
            CommerceOperationKind.CatalogPurchase,
            PLAYER,
            "offer 9",
            CancellationToken.None
        );

        await _journal.TransitionAsync(
            id,
            CommerceOperationState.Pivoted,
            null,
            null,
            CancellationToken.None
        );

        IReadOnlyList<CommerceOperationRecord> stuck = await _journal.GetIncompletePivotedAsync(
            10,
            CancellationToken.None
        );
        stuck.Should().ContainSingle().Which.Detail.Should().Be("offer 9");

        await _journal.TransitionAsync(
            id,
            CommerceOperationState.Completed,
            null,
            null,
            CancellationToken.None
        );

        (await _journal.GetIncompletePivotedAsync(10, CancellationToken.None)).Should().BeEmpty();
    }

    /// <summary>A failure before the pivot is not recovery's problem — it was compensated.</summary>
    [Fact]
    public async Task AnOperationThatFailedBeforeItsPivot_IsNotRecoveryWork()
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
            CommerceOperationState.FailedBeforePivot,
            CommerceStepKeys.DEBIT,
            "insufficient balance",
            CancellationToken.None
        );

        (await _journal.GetIncompletePivotedAsync(10, CancellationToken.None)).Should().BeEmpty();
    }

    /// <summary>
    /// Every flow records its pivot from inside the closure <c>ExecutePurchaseAsync</c> runs, and
    /// that closure refunds the debit on any exception leaving it. So a journal write that threw
    /// after the goods were handed over refunded a delivered purchase: the player kept the item and
    /// got the credits back. Recording the pivot must not be able to undo it.
    /// </summary>
    [Fact]
    public async Task APivotThatCannotBeRecorded_DoesNotThrowAtTheCaller()
    {
        CommerceOperationId id = CommerceOperationId.New();
        await _journal.OpenAsync(
            id,
            CommerceOperationKind.CatalogPurchase,
            PLAYER,
            null,
            CancellationToken.None
        );

        // Closing the connection takes the in-memory database with it: every write from here fails.
        await _conn.CloseAsync();

        Func<Task> recordThePivot = () =>
            _journal.TransitionAsync(
                id,
                CommerceOperationState.Pivoted,
                CommerceStepKeys.LOCAL_GRANT,
                null,
                CancellationToken.None
            );

        await recordThePivot
            .Should()
            .NotThrowAsync(
                "the goods are already the player's and the caller compensates on throw"
            );
    }

    /// <summary>
    /// The counterweight: the guard above must not have turned the journal into something that
    /// swallows everything. Before the pivot a refund is still the right answer, so the caller has
    /// to hear about the failure.
    /// </summary>
    [Fact]
    public async Task ADebitThatCannotBeRecorded_StillThrows()
    {
        CommerceOperationId id = CommerceOperationId.New();
        await _journal.OpenAsync(
            id,
            CommerceOperationKind.CatalogPurchase,
            PLAYER,
            null,
            CancellationToken.None
        );

        await _conn.CloseAsync();

        Func<Task> recordTheDebit = () =>
            _journal.TransitionAsync(
                id,
                CommerceOperationState.Debited,
                CommerceStepKeys.DEBIT,
                null,
                CancellationToken.None
            );

        await recordTheDebit
            .Should()
            .ThrowAsync<Exception>("compensation is still correct before the pivot");
    }

    /// <summary>
    /// What the four <c>DbUpdateException</c> sites now ask instead of reading the exception type.
    /// They report "already applied" on a false answer, and a caller told that about a write which
    /// never landed grants the goods against a debit that was rolled back.
    /// </summary>
    [Fact]
    public async Task ACommittedReceipt_IsTellableFromAnyOtherWriteFailure()
    {
        CommerceOperationId recorded = CommerceOperationId.New();
        CommerceOperationId never = CommerceOperationId.New();

        await _journal.OpenAsync(
            recorded,
            CommerceOperationKind.CatalogPurchase,
            PLAYER,
            null,
            CancellationToken.None
        );
        await _journal.TryRecordStepAsync(
            recorded,
            CommerceStepKeys.DEBIT,
            "applied",
            CancellationToken.None
        );

        await using VortexDbContext db = new(
            new DbContextOptionsBuilder<VortexDbContext>().UseSqlite(_conn).Options
        );

        (
            await db.HasCommittedReceiptAsync(
                recorded,
                CommerceStepKeys.DEBIT,
                CancellationToken.None
            )
        )
            .Should()
            .BeTrue("this one really did run before");

        (await db.HasCommittedReceiptAsync(never, CommerceStepKeys.DEBIT, CancellationToken.None))
            .Should()
            .BeFalse("no receipt means the write failed for some other reason, not a replay");

        (
            await db.HasCommittedReceiptAsync(
                recorded,
                CommerceStepKeys.LOCAL_GRANT,
                CancellationToken.None
            )
        )
            .Should()
            .BeFalse("a receipt for one step is not a receipt for another");
    }

    private async Task<DateTime?> PivotedAsync(CommerceOperationId id)
    {
        IReadOnlyList<CommerceOperationRecord> rows = await _journal.GetIncompletePivotedAsync(
            10,
            CancellationToken.None
        );

        return rows.Single(r => r.Id == id).PivotedAt;
    }

    private sealed class TestDbContextFactory(DbContextOptions<VortexDbContext> options)
        : IDbContextFactory<VortexDbContext>
    {
        public VortexDbContext CreateDbContext() => new(options);
    }
}
