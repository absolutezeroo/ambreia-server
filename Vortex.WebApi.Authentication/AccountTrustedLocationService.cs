using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vortex.Database.Context;
using Vortex.Database.Entities.Players;
using Vortex.Primitives.Authentication;

namespace Vortex.WebApi.Authentication;

/// <summary>
/// Owns <c>player_account_trusted_locations</c>: the places an account has already answered from.
/// </summary>
public sealed class AccountTrustedLocationService(
    IDbContextFactory<VortexDbContext> dbContextFactory,
    IOptions<AuthenticationConfig> options,
    ILogger<AccountTrustedLocationService> logger
) : IAccountTrustedLocationService
{
    private readonly IDbContextFactory<VortexDbContext> _dbContextFactory = dbContextFactory;
    private readonly IOptions<AuthenticationConfig> _options = options;
    private readonly ILogger<AccountTrustedLocationService> _logger = logger;

    public string Fingerprint(string? address, string? userAgent)
    {
        // Keyed, not a bare hash: a plain SHA-256 of an IPv4 address is reversible by trying all
        // four billion of them, which would make this table exactly the log of player addresses it
        // exists not to be. The key is the one the rest of the auth path already hashes addresses
        // with.
        byte[] key = Encoding.UTF8.GetBytes(_options.Value.IpHashSecret);
        byte[] value = Encoding.UTF8.GetBytes(
            $"{address ?? string.Empty}\n{userAgent ?? string.Empty}"
        );

        return Convert.ToHexStringLower(HMACSHA256.HashData(key, value));
    }

    public async Task<bool> IsTrustedAsync(
        int accountId,
        string fingerprint,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrEmpty(fingerprint))
        {
            return false;
        }

        await using VortexDbContext db = await _dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        return await db
            .PlayerAccountTrustedLocations.AsNoTracking()
            .AnyAsync(l => l.PlayerAccountEntityId == accountId && l.Fingerprint == fingerprint, ct)
            .ConfigureAwait(false);
    }

    public async Task TrustAsync(int accountId, string fingerprint, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(fingerprint))
        {
            return;
        }

        await using VortexDbContext db = await _dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        PlayerAccountTrustedLocationEntity? row = await db
            .PlayerAccountTrustedLocations.FirstOrDefaultAsync(
                l => l.PlayerAccountEntityId == accountId && l.Fingerprint == fingerprint,
                ct
            )
            .ConfigureAwait(false);

        if (row is null)
        {
            db.PlayerAccountTrustedLocations.Add(
                new PlayerAccountTrustedLocationEntity
                {
                    PlayerAccountEntityId = accountId,
                    Fingerprint = fingerprint,
                    LastSeenAt = DateTime.UtcNow,
                }
            );
        }
        else
        {
            row.LastSeenAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> ResetAsync(int accountId, CancellationToken ct = default)
    {
        await using VortexDbContext db = await _dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        // Tracked and removed rather than ExecuteDelete: the test host runs on EF InMemory, which
        // does not implement the bulk operations.
        List<PlayerAccountTrustedLocationEntity> rows = await db
            .PlayerAccountTrustedLocations.Where(l => l.PlayerAccountEntityId == accountId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (rows.Count == 0)
        {
            return 0;
        }

        db.PlayerAccountTrustedLocations.RemoveRange(rows);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Account {AccountId} forgot {Count} trusted location(s)",
            accountId,
            rows.Count
        );

        return rows.Count;
    }
}
