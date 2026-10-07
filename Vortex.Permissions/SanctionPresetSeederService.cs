using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vortex.Database.Context;
using Vortex.Database.Entities.Permissions;

namespace Vortex.Permissions;

/// <summary>
/// Bootstraps <see cref="DefaultSanctionPresets"/> into a fresh database at startup. Idempotent and
/// additive-only, mirroring <see cref="PermissionSeederService"/>: a preset is seeded once, at
/// creation; an existing (Kind, PresetIndex) row is left exactly as the administrator configured it.
/// </summary>
internal sealed class SanctionPresetSeederService(
    IDbContextFactory<VortexDbContext> dbContextFactory,
    ILogger<SanctionPresetSeederService> logger
) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            VortexDbContext db = await dbContextFactory
                .CreateDbContextAsync(cancellationToken)
                .ConfigureAwait(false);

            try
            {
                int seeded = 0;

                foreach (DefaultSanctionPresets.PresetSeed seed in DefaultSanctionPresets.All)
                {
                    bool exists = await db
                        .SanctionPresets.AsNoTracking()
                        .AnyAsync(
                            p => p.Kind == seed.Kind && p.PresetIndex == seed.PresetIndex,
                            cancellationToken
                        )
                        .ConfigureAwait(false);

                    if (exists)
                    {
                        continue;
                    }

                    db.SanctionPresets.Add(
                        new SanctionPresetEntity
                        {
                            Kind = seed.Kind,
                            PresetIndex = seed.PresetIndex,
                            Name = seed.Name,
                            DurationSeconds = seed.DurationSeconds,
                        }
                    );
                    seeded++;
                }

                if (seeded > 0)
                {
                    await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                    logger.LogInformation("Seeded {Count} default sanction preset(s)", seeded);
                }
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
                "Sanction preset seeding failed; ensure the sanction_presets migration has been applied."
            );
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
