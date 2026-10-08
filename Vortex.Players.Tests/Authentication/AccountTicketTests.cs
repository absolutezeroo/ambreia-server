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
using Vortex.Players.Tests.Support;
using Vortex.Primitives.Authentication;
using Vortex.Primitives.Events;
using Vortex.Primitives.Players.Enums;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Players.Tests.Authentication;

/// <summary>
/// Spec F3a §5.1: a ticket names a player (straight into the world) or an account (the character
/// screen); both follow the same expiry rules.
/// </summary>
public sealed class AccountTicketTests
{
    private readonly DbContextOptions<VortexDbContext> _options =
        new DbContextOptionsBuilder<VortexDbContext>()
            .UseInMemoryDatabase($"account-tickets-{Guid.NewGuid():N}")
            .Options;

    private readonly List<IEvent> _events = [];

    [Fact]
    public async Task AnAccountTicket_ResolvesToTheAccount()
    {
        int account = await SeedAccountAsync();
        await SeedAccountTicketAsync(account, "account-ticket", DateTime.UtcNow, null);

        TicketOwner owner = await Service().ResolveTicketAsync("account-ticket");

        owner.Should().Be(new TicketOwner(0, account));
    }

    [Fact]
    public async Task AnExpiredAccountTicket_ResolvesToNoOne()
    {
        int account = await SeedAccountAsync();
        await SeedAccountTicketAsync(
            account,
            "stale",
            DateTime.UtcNow.AddMinutes(-10),
            DateTime.UtcNow.AddMinutes(-5)
        );

        (await Service().ResolveTicketAsync("stale")).Should().Be(TicketOwner.None);
        _events.Should().ContainSingle(e => e is PlayerLoginFailedEvent);
    }

    [Fact]
    public async Task APlayerTicket_StillResolvesToThePlayer()
    {
        await using VortexDbContext db = new(_options);
        PlayerEntity player = new()
        {
            Name = "alice",
            Figure = "hd-180-1",
            Gender = AvatarGenderType.Male,
            PlayerStatus = PlayerStatusType.Offline,
            PlayerPerks = PlayerPerkFlags.None,
        };
        db.Players.Add(player);
        db.SecurityTickets.Add(
            new SecurityTicketEntity
            {
                PlayerEntityId = player.Id,
                PlayerEntity = player,
                Ticket = "player-ticket",
                IpAddress = "127.0.0.1",
                CreatedAt = DateTime.UtcNow,
            }
        );
        await db.SaveChangesAsync();

        (await Service().ResolveTicketAsync("player-ticket"))
            .Should()
            .Be(new TicketOwner(player.Id, 0));
    }

    [Fact]
    public async Task AnUnknownTicket_ResolvesToNoOne() =>
        (await Service().ResolveTicketAsync("nope")).Should().Be(TicketOwner.None);

    private AuthenticationService Service() =>
        new(
            new OptionsDbContextFactory(_options),
            FakeProxy.Create<IEventPublisher>(call =>
            {
                _events.Add((IEvent)call.Args![0]!);

                return Task.CompletedTask;
            }),
            Options.Create(
                new AuthenticationConfig { IpHashSecret = "test-secret", TicketTtlSeconds = 30 }
            )
        );

    private async Task<int> SeedAccountAsync()
    {
        await using VortexDbContext db = new(_options);
        PlayerAccountEntity account = new() { Email = "a@b.c", PasswordHash = "x" };
        db.PlayerAccounts.Add(account);
        await db.SaveChangesAsync();

        return account.Id;
    }

    private async Task SeedAccountTicketAsync(
        int account,
        string ticket,
        DateTime createdAt,
        DateTime? expiresAt
    )
    {
        await using VortexDbContext db = new(_options);
        db.AccountTickets.Add(
            new AccountTicketEntity
            {
                AccountId = account,
                Ticket = ticket,
                IpAddress = "127.0.0.1",
                CreatedAt = createdAt,
                ExpiresAt = expiresAt,
            }
        );
        await db.SaveChangesAsync();
    }
}
