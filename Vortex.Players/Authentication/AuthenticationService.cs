using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Vortex.Database.Context;
using Vortex.Database.Entities.Players;
using Vortex.Database.Entities.Security;
using Vortex.Primitives.Authentication;
using Vortex.Primitives.Events;

namespace Vortex.Players.Authentication;

public sealed class AuthenticationService(
    IDbContextFactory<VortexDbContext> dbCtxFactory,
    IEventPublisher events,
    IOptions<AuthenticationConfig> options
) : IAuthenticationService
{
    private readonly IDbContextFactory<VortexDbContext> _dbCtxFactory = dbCtxFactory;
    private readonly IEventPublisher _events = events;
    private readonly string _ipHashSecret = options.Value.IpHashSecret;
    private readonly int _ticketTtlSeconds = options.Value.TicketTtlSeconds;
    private readonly bool _ticketSingleUse = options.Value.TicketSingleUse;
    private readonly int? _ticketAbsoluteLifetimeSeconds = options
        .Value
        .TicketAbsoluteLifetimeSeconds;

    public async Task<int> GetPlayerIdFromTicketAsync(
        string ticket,
        string? remoteIp = null,
        CancellationToken ct = default
    )
    {
        if (ticket is null || ticket.Length == 0)
        {
            return 0;
        }

        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        SecurityTicketEntity? entity = await dbCtx
            .SecurityTickets.FirstOrDefaultAsync(entity => entity.Ticket == ticket, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            await _events
                .PublishAsync(new PlayerLoginFailedEvent(HashIp(remoteIp)), ct)
                .ConfigureAwait(false);

            return 0;
        }

        return await ConsumePlayerTicketAsync(dbCtx, entity, remoteIp, ct).ConfigureAwait(false);
    }

    /// <summary>A player ticket found: expired ones fail (and go, unless locked), others slide or
    /// are consumed. The player id, or 0.</summary>
    private async Task<int> ConsumePlayerTicketAsync(
        VortexDbContext dbCtx,
        SecurityTicketEntity entity,
        string? remoteIp,
        CancellationToken ct
    )
    {
        DateTime now = DateTime.UtcNow;

        if (IsExpired(entity.CreatedAt, entity.ExpiresAt, now))
        {
            if (!entity.IsLocked)
            {
                dbCtx.SecurityTickets.Remove(entity);

                await dbCtx.SaveChangesAsync(ct).ConfigureAwait(false);
            }

            await _events
                .PublishAsync(new PlayerLoginFailedEvent(HashIp(remoteIp)), ct)
                .ConfigureAwait(false);

            return 0;
        }

        // `IsLocked` tickets are persistent ones written outside the emulator (the web API only
        // mints unlocked ones) and intentionally never touched here
        // (no slide, no single-use deletion, no expiry) — they are a separate mechanism from the
        // one-ticket-per-login-attempt flow this method otherwise implements.
        if (!entity.IsLocked)
        {
            if (_ticketSingleUse)
            {
                // Consumed on first successful use: an observed ticket (proxy logs, browser
                // history, unencrypted transport, Referer) can no longer be replayed at all,
                // rather than being able to extend its own validity indefinitely.
                dbCtx.SecurityTickets.Remove(entity);
            }
            else
            {
                // Refresh the expiry so the client can reconnect after a disconnect without a
                // new ticket, while keeping the replay window bounded per use. Left default
                // (TicketSingleUse = false) because the client re-reads its ticket from ?sso= on
                // a page reload.
                entity.ExpiresAt = SlidExpiry(entity.CreatedAt, now);
            }

            await dbCtx.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        await _events
            .PublishAsync(new PlayerLoggedInEvent(entity.PlayerEntityId, HashIp(remoteIp)), ct)
            .ConfigureAwait(false);

        return entity.PlayerEntityId;
    }

    public async Task<TicketOwner> ResolveTicketAsync(
        string ticket,
        string? remoteIp = null,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrEmpty(ticket))
        {
            return TicketOwner.None;
        }

        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        // A player ticket keeps its own path, events included; 0 there means expired.
        SecurityTicketEntity? playerTicket = await dbCtx
            .SecurityTickets.FirstOrDefaultAsync(t => t.Ticket == ticket, ct)
            .ConfigureAwait(false);

        if (playerTicket is not null)
        {
            int playerId = await ConsumePlayerTicketAsync(dbCtx, playerTicket, remoteIp, ct)
                .ConfigureAwait(false);

            return playerId > 0 ? new TicketOwner(playerId, 0) : TicketOwner.None;
        }

        AccountTicketEntity? entity = await dbCtx
            .AccountTickets.FirstOrDefaultAsync(t => t.Ticket == ticket, ct)
            .ConfigureAwait(false);
        DateTime now = DateTime.UtcNow;

        if (entity is null || IsExpired(entity.CreatedAt, entity.ExpiresAt, now))
        {
            if (entity is not null)
            {
                dbCtx.AccountTickets.Remove(entity);
                await dbCtx.SaveChangesAsync(ct).ConfigureAwait(false);
            }

            await _events
                .PublishAsync(new PlayerLoginFailedEvent(HashIp(remoteIp)), ct)
                .ConfigureAwait(false);

            return TicketOwner.None;
        }

        // Same rules as a player ticket: consumed when single-use, otherwise slid.
        if (_ticketSingleUse)
        {
            dbCtx.AccountTickets.Remove(entity);
        }
        else
        {
            entity.ExpiresAt = SlidExpiry(entity.CreatedAt, now);
        }

        await dbCtx.SaveChangesAsync(ct).ConfigureAwait(false);

        return new TicketOwner(0, entity.AccountId);
    }

    public async Task<string?> IssuePlayerTicketAsync(
        int playerId,
        string? remoteIp,
        CancellationToken ct = default
    )
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        PlayerEntity? player = await dbCtx
            .Players.FirstOrDefaultAsync(p => p.Id == playerId, ct)
            .ConfigureAwait(false);

        if (player is null)
        {
            return null;
        }

        // One ticket per player: a new sign-in replaces the last.
        SecurityTicketEntity? existing = await dbCtx
            .SecurityTickets.FirstOrDefaultAsync(t => t.PlayerEntityId == playerId, ct)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            dbCtx.SecurityTickets.Remove(existing);
        }

        string ticket = NewTicket();

        dbCtx.SecurityTickets.Add(
            new SecurityTicketEntity
            {
                PlayerEntityId = playerId,
                Ticket = ticket,
                IpAddress = IpOrLoopback(remoteIp),
                IsLocked = false,
                PlayerEntity = player,
            }
        );

        await dbCtx.SaveChangesAsync(ct).ConfigureAwait(false);

        return ticket;
    }

    public async Task<string> IssueAccountTicketAsync(
        int accountId,
        string? remoteIp,
        CancellationToken ct = default
    )
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        // One ticket per account, as one per player.
        AccountTicketEntity? existing = await dbCtx
            .AccountTickets.FirstOrDefaultAsync(t => t.AccountId == accountId, ct)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            dbCtx.AccountTickets.Remove(existing);
        }

        string ticket = NewTicket();

        dbCtx.AccountTickets.Add(
            new AccountTicketEntity
            {
                AccountId = accountId,
                Ticket = ticket,
                IpAddress = IpOrLoopback(remoteIp),
            }
        );

        await dbCtx.SaveChangesAsync(ct).ConfigureAwait(false);

        return ticket;
    }

    /// <summary>256 bits from the cryptographic generator, hex: the ticket is the whole credential.
    /// Two GUIDs were random by accident, not by contract.</summary>
    private static string NewTicket() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    private static string IpOrLoopback(string? remoteIp) =>
        string.IsNullOrWhiteSpace(remoteIp) ? "127.0.0.1" : remoteIp;

    public async Task RecordLoginAsync(
        int playerId,
        string? remoteIp,
        CancellationToken ct = default
    ) =>
        await _events
            .PublishAsync(new PlayerLoggedInEvent(playerId, HashIp(remoteIp)), ct)
            .ConfigureAwait(false);

    /// <summary>Expired once past <c>ExpiresAt</c>, or past <c>CreatedAt + TTL</c> before a first use.</summary>
    private bool IsExpired(DateTime createdAt, DateTime? expiresAt, DateTime now)
    {
        DateTime? expiry =
            expiresAt ?? (_ticketTtlSeconds > 0 ? createdAt.AddSeconds(_ticketTtlSeconds) : null);

        return expiry.HasValue && now > expiry.Value;
    }

    /// <summary>The expiry after a successful use: one TTL more, capped by the absolute lifetime;
    /// null when there is no TTL.</summary>
    private DateTime? SlidExpiry(DateTime createdAt, DateTime now)
    {
        DateTime slid =
            _ticketTtlSeconds > 0 ? now.AddSeconds(_ticketTtlSeconds) : DateTime.MaxValue;

        if (_ticketAbsoluteLifetimeSeconds is int absoluteSeconds)
        {
            DateTime cap = createdAt.AddSeconds(absoluteSeconds);

            if (slid > cap)
            {
                slid = cap;
            }
        }

        return slid == DateTime.MaxValue ? null : slid;
    }

    private string? HashIp(string? remoteIp)
    {
        if (string.IsNullOrWhiteSpace(remoteIp))
        {
            return null;
        }

        string key = _ipHashSecret;
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        byte[] keyBytes = Encoding.UTF8.GetBytes(key);
        byte[] ipBytes = Encoding.UTF8.GetBytes(remoteIp.Trim());

        byte[] hash = HMACSHA256.HashData(keyBytes, ipBytes);

        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
