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
            DateTime? expiry =
                entity.ExpiresAt
                ?? (_ticketTtlSeconds > 0 ? entity.CreatedAt.AddSeconds(_ticketTtlSeconds) : null);

            if (expiry.HasValue && now > expiry.Value)
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
                    DateTime slidExpiry =
                        _ticketTtlSeconds > 0
                            ? now.AddSeconds(_ticketTtlSeconds)
                            : DateTime.MaxValue;

                    if (_ticketAbsoluteLifetimeSeconds is int absoluteSeconds)
                    {
                        DateTime absoluteCap = entity.CreatedAt.AddSeconds(absoluteSeconds);

                        if (slidExpiry > absoluteCap)
                        {
                            slidExpiry = absoluteCap;
                        }
                    }

                    entity.ExpiresAt = slidExpiry == DateTime.MaxValue ? null : slidExpiry;
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
