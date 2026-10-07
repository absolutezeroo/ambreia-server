using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Vortex.Database.Context;
using Vortex.Database.Entities.Players;
using Vortex.Database.Entities.Security;
using Vortex.Players.Authentication;
using Vortex.Primitives.Authentication;
using Vortex.Primitives.Events;
using Vortex.Primitives.Players.Enums;
using Vortex.Primitives.Rooms.Enums;
using Xunit;

namespace Vortex.Players.Tests.Authentication;

/// <summary>
/// TEST-01 safety net for <see cref="AuthenticationService.GetPlayerIdFromTicketAsync"/>: ticket
/// lookup, the <c>ExpiresAt ?? CreatedAt + TicketTtlSeconds</c> expiry computation, the
/// locked-ticket exemption from deletion-on-expiry, the sliding-expiry refresh on a successful
/// login, and which of <see cref="PlayerLoggedInEvent"/> / <see cref="PlayerLoginFailedEvent"/>
/// gets published for each outcome.
/// </summary>
public sealed class AuthenticationServiceTests
{
    private const int TICKET_TTL_SECONDS = 30;

    private sealed class RecordingEventPublisher : IEventPublisher
    {
        public List<IEvent> Published { get; } = [];

        public Task PublishAsync(IEvent @event, CancellationToken ct = default)
        {
            Published.Add(@event);

            return Task.CompletedTask;
        }
    }

    private sealed class TestDbContextFactory(DbContextOptions<VortexDbContext> options)
        : IDbContextFactory<VortexDbContext>
    {
        public VortexDbContext CreateDbContext() => new(options);
    }

    private static DbContextOptions<VortexDbContext> NewOptions() =>
        new DbContextOptionsBuilder<VortexDbContext>()
            .UseInMemoryDatabase($"auth-{Guid.NewGuid():N}")
            .Options;

    private static AuthenticationService NewService(
        DbContextOptions<VortexDbContext> options,
        RecordingEventPublisher events,
        int ticketTtlSeconds = TICKET_TTL_SECONDS
    )
    {
        AuthenticationConfig config = new()
        {
            IpHashSecret = "test-secret",
            TicketTtlSeconds = ticketTtlSeconds,
        };

        return new AuthenticationService(
            new TestDbContextFactory(options),
            events,
            Options.Create(config)
        );
    }

    private static PlayerEntity NewPlayer(string name) =>
        new()
        {
            Name = name,
            Figure = "hr-115-42.hd-195-19.ch-3030-82.lg-275-1408.fa-1201.ca-1804-64",
            Gender = AvatarGenderType.Male,
            PlayerStatus = PlayerStatusType.Offline,
            PlayerPerks = PlayerPerkFlags.None,
        };

    private static async Task<PlayerEntity> SeedPlayerAsync(
        DbContextOptions<VortexDbContext> options,
        string name
    )
    {
        await using VortexDbContext ctx = new(options);
        PlayerEntity player = NewPlayer(name);
        ctx.Players.Add(player);
        await ctx.SaveChangesAsync().ConfigureAwait(true);

        return player;
    }

    private static async Task SeedTicketAsync(
        DbContextOptions<VortexDbContext> options,
        PlayerEntity player,
        string ticket,
        DateTime createdAt,
        DateTime? expiresAt,
        bool isLocked
    )
    {
        await using VortexDbContext ctx = new(options);
        // `player` was loaded/created against a different DbContext instance -- attach it as
        // Unchanged here first so SaveChanges does not try to re-INSERT an already-persisted row
        // via the required PlayerEntity navigation.
        ctx.Players.Attach(player);
        ctx.SecurityTickets.Add(
            new SecurityTicketEntity
            {
                PlayerEntityId = player.Id,
                PlayerEntity = player,
                Ticket = ticket,
                IpAddress = "127.0.0.1",
                IsLocked = isLocked,
                ExpiresAt = expiresAt,
                CreatedAt = createdAt,
            }
        );
        await ctx.SaveChangesAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task ValidNonExpiredTicket_ResolvesToTheCorrectPlayerId()
    {
        DbContextOptions<VortexDbContext> options = NewOptions();
        PlayerEntity player = await SeedPlayerAsync(options, "alice").ConfigureAwait(true);
        await SeedTicketAsync(
                options,
                player,
                "valid-ticket",
                createdAt: DateTime.UtcNow,
                expiresAt: DateTime.UtcNow.AddSeconds(60),
                isLocked: false
            )
            .ConfigureAwait(true);

        RecordingEventPublisher events = new();
        AuthenticationService service = NewService(options, events);

        int playerId = await service
            .GetPlayerIdFromTicketAsync("valid-ticket")
            .ConfigureAwait(true);

        playerId.Should().Be(player.Id);
    }

    [Fact]
    public async Task ValidTicket_PublishesPlayerLoggedInEvent()
    {
        DbContextOptions<VortexDbContext> options = NewOptions();
        PlayerEntity player = await SeedPlayerAsync(options, "bob").ConfigureAwait(true);
        await SeedTicketAsync(
                options,
                player,
                "valid-ticket",
                createdAt: DateTime.UtcNow,
                expiresAt: DateTime.UtcNow.AddSeconds(60),
                isLocked: false
            )
            .ConfigureAwait(true);

        RecordingEventPublisher events = new();
        AuthenticationService service = NewService(options, events);

        await service.GetPlayerIdFromTicketAsync("valid-ticket").ConfigureAwait(true);

        events.Published.Should().ContainSingle().Which.Should().BeOfType<PlayerLoggedInEvent>();
        ((PlayerLoggedInEvent)events.Published[0]).PlayerId.Should().Be(player.Id);
    }

    [Fact]
    public async Task MissingTicket_ReturnsZero_AndPublishesLoginFailedEvent()
    {
        DbContextOptions<VortexDbContext> options = NewOptions();
        RecordingEventPublisher events = new();
        AuthenticationService service = NewService(options, events);

        int playerId = await service
            .GetPlayerIdFromTicketAsync("does-not-exist")
            .ConfigureAwait(true);

        playerId.Should().Be(0);
        events.Published.Should().ContainSingle().Which.Should().BeOfType<PlayerLoginFailedEvent>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task NullOrEmptyTicket_ReturnsZero_WithoutTouchingTheDatabaseOrPublishing(
        string? ticket
    )
    {
        DbContextOptions<VortexDbContext> options = NewOptions();
        RecordingEventPublisher events = new();
        AuthenticationService service = NewService(options, events);

        int playerId = await service.GetPlayerIdFromTicketAsync(ticket!).ConfigureAwait(true);

        playerId.Should().Be(0);
        events.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task ExpiredNonLockedTicket_IsRejected_AndDeletedFromTheDatabase()
    {
        DbContextOptions<VortexDbContext> options = NewOptions();
        PlayerEntity player = await SeedPlayerAsync(options, "carol").ConfigureAwait(true);
        await SeedTicketAsync(
                options,
                player,
                "expired-ticket",
                createdAt: DateTime.UtcNow.AddMinutes(-10),
                expiresAt: DateTime.UtcNow.AddMinutes(-1),
                isLocked: false
            )
            .ConfigureAwait(true);

        RecordingEventPublisher events = new();
        AuthenticationService service = NewService(options, events);

        int playerId = await service
            .GetPlayerIdFromTicketAsync("expired-ticket")
            .ConfigureAwait(true);

        playerId.Should().Be(0);
        events.Published.Should().ContainSingle().Which.Should().BeOfType<PlayerLoginFailedEvent>();

        await using VortexDbContext verifyCtx = new(options);
        bool stillExists = await verifyCtx
            .SecurityTickets.AnyAsync(t => t.Ticket == "expired-ticket")
            .ConfigureAwait(true);
        stillExists.Should().BeFalse("an expired, non-locked ticket must be deleted");
    }

    [Fact]
    public async Task ExpiredButLockedTicket_IsRejected_ButNotDeleted()
    {
        DbContextOptions<VortexDbContext> options = NewOptions();
        PlayerEntity player = await SeedPlayerAsync(options, "dave").ConfigureAwait(true);
        await SeedTicketAsync(
                options,
                player,
                "locked-expired-ticket",
                createdAt: DateTime.UtcNow.AddMinutes(-10),
                expiresAt: DateTime.UtcNow.AddMinutes(-1),
                isLocked: true
            )
            .ConfigureAwait(true);

        RecordingEventPublisher events = new();
        AuthenticationService service = NewService(options, events);

        int playerId = await service
            .GetPlayerIdFromTicketAsync("locked-expired-ticket")
            .ConfigureAwait(true);

        // Current behaviour under test (not an endorsement): a locked ticket is rejected as
        // expired just like any other, but the IsLocked branch skips the delete.
        playerId.Should().Be(0);
        events.Published.Should().ContainSingle().Which.Should().BeOfType<PlayerLoginFailedEvent>();

        await using VortexDbContext verifyCtx = new(options);
        bool stillExists = await verifyCtx
            .SecurityTickets.AnyAsync(t => t.Ticket == "locked-expired-ticket")
            .ConfigureAwait(true);
        stillExists.Should().BeTrue("locked tickets are exempt from delete-on-expiry");
    }

    [Fact]
    public async Task ValidTicket_WithNullExpiresAt_FallsBackToCreatedAtPlusTtl_AndIsRejectedOnceStale()
    {
        DbContextOptions<VortexDbContext> options = NewOptions();
        PlayerEntity player = await SeedPlayerAsync(options, "erin").ConfigureAwait(true);
        // CreatedAt is far enough in the past that CreatedAt + TicketTtlSeconds has already
        // elapsed, with ExpiresAt left null so the TTL fallback is what's actually exercised.
        await SeedTicketAsync(
                options,
                player,
                "null-expiry-stale",
                createdAt: DateTime.UtcNow.AddSeconds(-(TICKET_TTL_SECONDS + 5)),
                expiresAt: null,
                isLocked: false
            )
            .ConfigureAwait(true);

        RecordingEventPublisher events = new();
        AuthenticationService service = NewService(options, events);

        int playerId = await service
            .GetPlayerIdFromTicketAsync("null-expiry-stale")
            .ConfigureAwait(true);

        playerId.Should().Be(0);
        events.Published.Should().ContainSingle().Which.Should().BeOfType<PlayerLoginFailedEvent>();
    }

    [Fact]
    public async Task ValidTicket_WithNullExpiresAt_WithinTtl_Succeeds()
    {
        DbContextOptions<VortexDbContext> options = NewOptions();
        PlayerEntity player = await SeedPlayerAsync(options, "frank").ConfigureAwait(true);
        await SeedTicketAsync(
                options,
                player,
                "null-expiry-fresh",
                createdAt: DateTime.UtcNow,
                expiresAt: null,
                isLocked: false
            )
            .ConfigureAwait(true);

        RecordingEventPublisher events = new();
        AuthenticationService service = NewService(options, events);

        int playerId = await service
            .GetPlayerIdFromTicketAsync("null-expiry-fresh")
            .ConfigureAwait(true);

        playerId.Should().Be(player.Id);
    }

    [Fact]
    public async Task ZeroTicketTtlSeconds_DisablesTheNullExpiryFallback_SoNullExpiryNeverExpires()
    {
        DbContextOptions<VortexDbContext> options = NewOptions();
        PlayerEntity player = await SeedPlayerAsync(options, "gina").ConfigureAwait(true);
        await SeedTicketAsync(
                options,
                player,
                "no-ttl-ticket",
                createdAt: DateTime.UtcNow.AddYears(-1),
                expiresAt: null,
                isLocked: false
            )
            .ConfigureAwait(true);

        RecordingEventPublisher events = new();
        AuthenticationService service = NewService(options, events, ticketTtlSeconds: 0);

        int playerId = await service
            .GetPlayerIdFromTicketAsync("no-ttl-ticket")
            .ConfigureAwait(true);

        playerId.Should().Be(player.Id);
    }

    [Fact]
    public async Task SuccessfulLogin_RefreshesExpiresAt_ForSlidingExpiry()
    {
        DbContextOptions<VortexDbContext> options = NewOptions();
        PlayerEntity player = await SeedPlayerAsync(options, "holly").ConfigureAwait(true);
        DateTime originalExpiry = DateTime.UtcNow.AddSeconds(5);
        await SeedTicketAsync(
                options,
                player,
                "sliding-ticket",
                createdAt: DateTime.UtcNow,
                expiresAt: originalExpiry,
                isLocked: false
            )
            .ConfigureAwait(true);

        RecordingEventPublisher events = new();
        AuthenticationService service = NewService(options, events);

        await service.GetPlayerIdFromTicketAsync("sliding-ticket").ConfigureAwait(true);

        await using VortexDbContext verifyCtx = new(options);
        SecurityTicketEntity refreshed = await verifyCtx
            .SecurityTickets.SingleAsync(t => t.Ticket == "sliding-ticket")
            .ConfigureAwait(true);

        refreshed
            .ExpiresAt.Should()
            .NotBe(originalExpiry, "a successful, unlocked login refreshes ExpiresAt");
        refreshed.ExpiresAt.Should().BeAfter(originalExpiry);
    }

    [Fact]
    public async Task SuccessfulLogin_OnALockedTicket_DoesNotRefreshExpiresAt()
    {
        DbContextOptions<VortexDbContext> options = NewOptions();
        PlayerEntity player = await SeedPlayerAsync(options, "ian").ConfigureAwait(true);
        DateTime originalExpiry = DateTime.UtcNow.AddSeconds(5);
        await SeedTicketAsync(
                options,
                player,
                "locked-active-ticket",
                createdAt: DateTime.UtcNow,
                expiresAt: originalExpiry,
                isLocked: true
            )
            .ConfigureAwait(true);

        RecordingEventPublisher events = new();
        AuthenticationService service = NewService(options, events);

        int playerId = await service
            .GetPlayerIdFromTicketAsync("locked-active-ticket")
            .ConfigureAwait(true);

        // A locked but not-yet-expired ticket still resolves to the player...
        playerId.Should().Be(player.Id);

        // ...but the IsLocked branch means the sliding-expiry refresh is skipped.
        await using VortexDbContext verifyCtx = new(options);
        SecurityTicketEntity unchanged = await verifyCtx
            .SecurityTickets.SingleAsync(t => t.Ticket == "locked-active-ticket")
            .ConfigureAwait(true);
        unchanged.ExpiresAt.Should().Be(originalExpiry);
    }

    [Fact]
    public async Task RemoteIp_IsHashed_NotStoredOrPublishedInTheClear()
    {
        DbContextOptions<VortexDbContext> options = NewOptions();
        PlayerEntity player = await SeedPlayerAsync(options, "jack").ConfigureAwait(true);
        await SeedTicketAsync(
                options,
                player,
                "ip-ticket",
                createdAt: DateTime.UtcNow,
                expiresAt: DateTime.UtcNow.AddSeconds(60),
                isLocked: false
            )
            .ConfigureAwait(true);

        RecordingEventPublisher events = new();
        AuthenticationService service = NewService(options, events);

        await service
            .GetPlayerIdFromTicketAsync("ip-ticket", remoteIp: "203.0.113.42")
            .ConfigureAwait(true);

        PlayerLoggedInEvent published =
            events.Published.Should().ContainSingle().Which as PlayerLoggedInEvent
            ?? throw new InvalidOperationException("expected a PlayerLoggedInEvent");

        published.IpHash.Should().NotBeNullOrEmpty();
        published.IpHash.Should().NotBe("203.0.113.42");
    }
}
