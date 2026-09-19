using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Context;
using Vortex.Database.Entities.Moderation;
using Vortex.Database.Entities.Players;
using Vortex.Primitives.Events;
using Vortex.Primitives.Moderation;
using Vortex.Primitives.Players.Enums;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Moderation;

/// <summary>
/// A reporter contesting the closure of their own report. The request carries a ticket id and
/// nothing else, so every condition the client's appeal button tests has to hold here too —
/// otherwise a crafted id appeals somebody else's report, an open one, or the same one twice.
/// </summary>
/// <remarks>
/// The button is offered on a report that is <em>closed</em> and was closed <em>without</em> a
/// sanction (<c>MyReportStatus.as:212</c>). This is the reporter saying "you decided nothing
/// happened and I disagree", never a sanctioned player contesting their own ban.
/// </remarks>
public sealed class CfhAppealTests
{
    private const int REPORTER = 10;
    private const int OTHER_REPORTER = 11;

    [Fact]
    public async Task Appeal_MarksAClosedUnsanctionedReportPending()
    {
        DbContextOptions<VortexDbContext> options = NewOptions();

        await using (VortexDbContext db = new(options))
        {
            db.CfhTickets.Add(NewClosed(1, REPORTER, sanctioned: false));
            await db.SaveChangesAsync();
        }

        (await NewService(options).AppealTicketAsync(1, REPORTER)).Should().BeTrue();

        await using VortexDbContext check = new(options);
        CfhTicketEntity ticket = await check.CfhTickets.SingleAsync(t => t.Id == 1);

        ticket.AppealStatus.Should().Be(CfhAppealStatus.Pending);
        ticket.AppealCreatedAt.Should().NotBeNull();
        ticket.AppealResolvedAt.Should().BeNull();
        // The original decision is not rewritten: the client still prints the close date under
        // "reported" while the appeal is the thing now pending.
        ticket.State.Should().Be(CfhTicketState.Closed);
        ticket.ClosedAt.Should().NotBeNull();
    }

    [Theory]
    // Somebody else's report — the id is all the request carries, so this is the crafted case.
    [InlineData(OTHER_REPORTER, CfhTicketState.Closed, false, CfhAppealStatus.None)]
    // Still open: there is no decision to contest yet.
    [InlineData(REPORTER, CfhTicketState.Open, false, CfhAppealStatus.None)]
    [InlineData(REPORTER, CfhTicketState.Picked, false, CfhAppealStatus.None)]
    // Closed *with* a sanction: the client greys the button out, and the reporter got what they
    // asked for anyway.
    [InlineData(REPORTER, CfhTicketState.Closed, true, CfhAppealStatus.None)]
    // Already appealed — one appeal per report, and a second must not restart the clock.
    [InlineData(REPORTER, CfhTicketState.Closed, false, CfhAppealStatus.Pending)]
    [InlineData(REPORTER, CfhTicketState.Closed, false, CfhAppealStatus.NoAction)]
    public async Task Appeal_IsRefusedOnEveryCaseTheButtonWouldNotHaveOffered(
        int ownerId,
        CfhTicketState state,
        bool sanctioned,
        CfhAppealStatus appealStatus
    )
    {
        DbContextOptions<VortexDbContext> options = NewOptions();
        DateTime? appealedAt =
            appealStatus == CfhAppealStatus.None ? null : DateTime.UtcNow.AddDays(-1);

        await using (VortexDbContext db = new(options))
        {
            CfhTicketEntity ticket = NewClosed(1, ownerId, sanctioned);
            ticket.State = state;
            ticket.AppealStatus = appealStatus;
            ticket.AppealCreatedAt = appealedAt;

            db.CfhTickets.Add(ticket);
            await db.SaveChangesAsync();
        }

        (await NewService(options).AppealTicketAsync(1, REPORTER)).Should().BeFalse();

        await using VortexDbContext check = new(options);
        CfhTicketEntity stored = await check.CfhTickets.SingleAsync(t => t.Id == 1);

        stored.AppealStatus.Should().Be(appealStatus);
        stored.AppealCreatedAt.Should().Be(appealedAt);
    }

    [Fact]
    public async Task Appeal_IgnoresASoftDeletedReport()
    {
        DbContextOptions<VortexDbContext> options = NewOptions();

        await using (VortexDbContext db = new(options))
        {
            CfhTicketEntity withdrawn = NewClosed(1, REPORTER, sanctioned: false);
            withdrawn.DeletedAt = DateTime.UtcNow;

            db.CfhTickets.Add(withdrawn);
            await db.SaveChangesAsync();
        }

        (await NewService(options).AppealTicketAsync(1, REPORTER)).Should().BeFalse();
    }

    [Fact]
    public async Task Appeal_RefusesAnUnboundSession()
    {
        (await NewService(NewOptions()).AppealTicketAsync(1, 0)).Should().BeFalse();
        (await NewService(NewOptions()).AppealTicketAsync(0, REPORTER)).Should().BeFalse();
    }

    [Fact]
    public async Task PendingAppeals_AreTheWorkListOldestFirst()
    {
        DbContextOptions<VortexDbContext> options = NewOptions();

        await using (VortexDbContext db = new(options))
        {
            CfhTicketEntity newer = NewClosed(1, REPORTER, sanctioned: false);
            newer.AppealStatus = CfhAppealStatus.Pending;
            newer.AppealCreatedAt = DateTime.UtcNow.AddHours(-1);

            CfhTicketEntity older = NewClosed(2, OTHER_REPORTER, sanctioned: false);
            older.AppealStatus = CfhAppealStatus.Pending;
            older.AppealCreatedAt = DateTime.UtcNow.AddHours(-5);

            CfhTicketEntity settled = NewClosed(3, REPORTER, sanctioned: false);
            settled.AppealStatus = CfhAppealStatus.NoAction;
            settled.AppealCreatedAt = DateTime.UtcNow.AddHours(-9);
            settled.AppealResolvedAt = DateTime.UtcNow.AddHours(-8);

            await SeedPlayersAsync(db);
            await db.CfhTickets.AddRangeAsync(newer, older, settled, NewClosed(4, REPORTER, false));
            await db.SaveChangesAsync();
        }

        ImmutableArray<CfhAppealSnapshot> appeals = await NewService(options)
            .GetPendingAppealsAsync();

        // Oldest first: an appeal nobody has answered is a person waiting, and the queue is worked
        // from the top. A resolved one and a never-appealed one are not work.
        appeals.Select(a => a.IssueId).Should().Equal(2, 1);
    }

    [Theory]
    [InlineData(true, CfhAppealStatus.ActionTaken, true)]
    [InlineData(false, CfhAppealStatus.NoAction, false)]
    public async Task ResolveAppeals_SettlesThePendingOnesAndSanctionsOnlyWhenUpheld(
        bool upheld,
        CfhAppealStatus expected,
        bool expectedSanctioned
    )
    {
        DbContextOptions<VortexDbContext> options = NewOptions();

        await using (VortexDbContext db = new(options))
        {
            CfhTicketEntity pending = NewClosed(1, REPORTER, sanctioned: false);
            pending.AppealStatus = CfhAppealStatus.Pending;
            pending.AppealCreatedAt = DateTime.UtcNow.AddHours(-2);

            // Never appealed: naming it in the request must not invent an appeal for it.
            await db.CfhTickets.AddRangeAsync(pending, NewClosed(2, REPORTER, sanctioned: false));
            await db.SaveChangesAsync();
        }

        ImmutableArray<int> resolved = await NewService(options)
            .ResolveAppealsAsync([1, 2], upheld);

        resolved.Should().Equal(1);

        await using VortexDbContext check = new(options);

        CfhTicketEntity settled = await check.CfhTickets.SingleAsync(t => t.Id == 1);
        settled.AppealStatus.Should().Be(expected);
        settled.AppealResolvedAt.Should().NotBeNull();
        settled.Sanctioned.Should().Be(expectedSanctioned);

        CfhTicketEntity untouched = await check.CfhTickets.SingleAsync(t => t.Id == 2);
        untouched.AppealStatus.Should().Be(CfhAppealStatus.None);
        untouched.AppealResolvedAt.Should().BeNull();
    }

    /// <summary>
    /// The three trailing wire fields come from these columns, and the client gates the whole
    /// appeal half of its window on them. A report history that reported <c>None</c> after an
    /// appeal would leave the player looking at "decided" on something they had contested.
    /// </summary>
    [Fact]
    public async Task ReportHistory_CarriesTheAppealThroughToTheSnapshot()
    {
        DbContextOptions<VortexDbContext> options = NewOptions();
        DateTime appealedAt = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

        await using (VortexDbContext db = new(options))
        {
            CfhTicketEntity appealed = NewClosed(1, REPORTER, sanctioned: false);
            appealed.AppealStatus = CfhAppealStatus.Pending;
            appealed.AppealCreatedAt = appealedAt;

            await db.CfhTickets.AddRangeAsync(appealed, NewClosed(2, REPORTER, sanctioned: false));
            await db.SaveChangesAsync();
        }

        ImmutableArray<CfhReportStatusSnapshot> history = await NewService(options)
            .GetReportHistoryForReporterAsync(REPORTER);

        CfhReportStatusSnapshot appealedRow = history.Single(r => r.Id == 1);
        appealedRow.AppealStatus.Should().Be(CfhAppealStatus.Pending);
        appealedRow
            .AppealCreationTime.Should()
            .Be(new DateTimeOffset(appealedAt).ToUnixTimeMilliseconds());
        // -1, not 0: the client prints "-" on that exact value and a 1970 date on anything else.
        appealedRow.AppealResolutionTime.Should().Be(-1);

        CfhReportStatusSnapshot plainRow = history.Single(r => r.Id == 2);
        plainRow.AppealStatus.Should().Be(CfhAppealStatus.None);
        plainRow.AppealCreationTime.Should().Be(-1);
        plainRow.AppealResolutionTime.Should().Be(-1);
    }

    /// <summary>
    /// The reporter and the reported player behind every ticket here. The appeal work list reads
    /// their names through a required navigation, which EF resolves as an inner join: without the
    /// rows, the list comes back empty rather than nameless.
    /// </summary>
    private static async Task SeedPlayersAsync(VortexDbContext db) =>
        await db.Players.AddRangeAsync(
            NewPlayer(REPORTER, "tester"),
            NewPlayer(OTHER_REPORTER, "other"),
            NewPlayer(99, "reported")
        );

    private static PlayerEntity NewPlayer(int id, string name) =>
        new()
        {
            Id = id,
            Name = name,
            Figure = "hd-1-1",
            Gender = AvatarGenderType.Male,
            PlayerStatus = PlayerStatusType.Offline,
            PlayerPerks = PlayerPerkFlags.None,
        };

    private static DbContextOptions<VortexDbContext> NewOptions() =>
        new DbContextOptionsBuilder<VortexDbContext>()
            .UseInMemoryDatabase($"cfh-appeal-{Guid.NewGuid():N}")
            .Options;

    private static ICfhTicketService NewService(DbContextOptions<VortexDbContext> options) =>
        new CfhTicketService(
            new SingleOptionsFactory(options),
            FakeProxy.Create<IEventPublisher>(_ => Task.CompletedTask)
        );

    private static CfhTicketEntity NewClosed(int id, int reporterId, bool sanctioned) =>
        new()
        {
            Id = id,
            State = CfhTicketState.Closed,
            ReporterPlayerEntityId = reporterId,
            ReportedPlayerEntityId = 99,
            CfhTopicEntityId = 1,
            Message = "he keeps flooding",
            CreatedAt = DateTime.UtcNow.AddDays(-2),
            ClosedAt = DateTime.UtcNow.AddDays(-1),
            CloseReason = CfhTicketCloseReason.Useless,
            Sanctioned = sanctioned,
        };

    private sealed class SingleOptionsFactory(DbContextOptions<VortexDbContext> options)
        : IDbContextFactory<VortexDbContext>
    {
        public VortexDbContext CreateDbContext() => new(options);
    }
}
