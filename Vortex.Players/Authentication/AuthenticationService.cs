using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Vortex.Database.Context;
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

        VortexDbContext dbCtx = await _dbCtxFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        try
        {
            if (dbCtx.SecurityTickets is null)
            {
                return 0;
            }

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
        finally
        {
            await dbCtx.DisposeAsync().ConfigureAwait(false);
        }
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
        if (await dbCtx.SecurityTickets.AnyAsync(t => t.Ticket == ticket, ct).ConfigureAwait(false))
        {
            int playerId = await GetPlayerIdFromTicketAsync(ticket, remoteIp, ct)
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
