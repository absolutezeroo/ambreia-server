using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vortex.Database.Context;
using Vortex.Database.Entities.Permissions;
using Vortex.Primitives.Authentication;

namespace Vortex.Permissions;

/// <summary>
/// Bootstraps the default roles into a fresh database at startup. Idempotent and additive-only: a
/// role's capabilities are seeded once, at creation; existing roles are left exactly as the
/// administrator configured them. Failures are logged, never fatal (e.g. before the migration runs).
/// </summary>
internal sealed class PermissionSeederService(
    IDbContextFactory<VortexDbContext> dbContextFactory,
    IOptions<AuthenticationConfig> config,
    ILogger<PermissionSeederService> logger
) : IHostedService
{
    private readonly AuthenticationConfig _config = config.Value;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            VortexDbContext db = await dbContextFactory
                .CreateDbContextAsync(cancellationToken)
                .ConfigureAwait(false);

            try
            {
                foreach (DefaultRoles.RoleSeed seed in DefaultRoles.All)
                {
                    bool exists = await db
                        .Roles.AsNoTracking()
                        .AnyAsync(r => r.Key == seed.Key, cancellationToken)
                        .ConfigureAwait(false);

                    if (exists)
                    {
                        continue;
                    }

                    RoleEntity role = new RoleEntity { Key = seed.Key, Name = seed.Name };
                    db.Roles.Add(role);
                    await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                    foreach (string capability in seed.Capabilities)
                    {
                        db.RolePermissions.Add(
                            new RolePermissionEntity
                            {
                                RoleEntityId = role.Id,
                                CapabilityKey = capability,
                            }
                        );
                    }

                    if (seed.Capabilities.Count > 0)
                    {
                        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    }

                    logger.LogInformation(
                        "Seeded default role '{RoleKey}' with {CapabilityCount} capabilities",
                        seed.Key,
                        seed.Capabilities.Count
                    );
                }

                await EnsureBootstrapOwnerAsync(db, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await db.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Permission role seeding failed; ensure the permissions migration has been applied."
            );
        }
    }

    /// <summary>
    /// Grants the configured bootstrap account the <c>owner</c> role so the very first administrator
    /// can sign in (e.g. to the admin dashboard) before any role has been assigned. Idempotent: does
    /// nothing if the email is unset, the account is missing, or the assignment already exists.
    /// </summary>
    private async Task EnsureBootstrapOwnerAsync(VortexDbContext db, CancellationToken ct)
    {
        string? email = _config.BootstrapOwnerEmail?.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(email))
        {
            return;
        }

        int? accountId = await db
            .PlayerAccounts.AsNoTracking()
            .Where(a => a.Email == email)
            .Select(a => (int?)a.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (accountId is null)
        {
            logger.LogWarning(
                "Bootstrap owner email '{Email}' has no matching account; skipping role assignment.",
                email
            );
            return;
        }

        int? ownerRoleId = await db
            .Roles.AsNoTracking()
            .Where(r => r.Key == DefaultRoles.OwnerKey)
            .Select(r => (int?)r.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (ownerRoleId is null)
        {
            return;
        }

        bool alreadyAssigned = await db
            .PlayerAccountRoles.AsNoTracking()
            .AnyAsync(
                x => x.PlayerAccountEntityId == accountId && x.RoleEntityId == ownerRoleId,
                ct
            )
            .ConfigureAwait(false);

        if (alreadyAssigned)
        {
            return;
        }

        db.PlayerAccountRoles.Add(
            new PlayerAccountRoleEntity
            {
                PlayerAccountEntityId = accountId.Value,
                RoleEntityId = ownerRoleId.Value,
            }
        );

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        logger.LogInformation(
            "Granted 'owner' role to bootstrap account {AccountId} ({Email}).",
            accountId,
            email
        );
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
