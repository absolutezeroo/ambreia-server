using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Vortex.Database.Backup;

/// <summary>
/// Takes a dump every <see cref="DatabaseBackupConfig.IntervalHours"/>, starting one interval after
/// boot rather than at boot: a server that restarts repeatedly would otherwise spend its startup
/// dumping, and push the useful history out of the retention window while doing it.
/// </summary>
public sealed class DatabaseBackupScheduler(
    IDatabaseBackupService backups,
    IOptions<DatabaseBackupConfig> config,
    ILogger<DatabaseBackupScheduler> logger
) : BackgroundService
{
    private readonly DatabaseBackupConfig _config = config.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!backups.IsConfigured || _config.IntervalHours <= 0)
        {
            logger.LogInformation(
                "Scheduled database backups are off (enabled={Enabled}, intervalHours={Interval})."
                    + " The dashboard can still take one on demand when a mysqldump path is set.",
                _config.Enabled,
                _config.IntervalHours
            );

            return;
        }

        TimeSpan interval = TimeSpan.FromHours(_config.IntervalHours);

        logger.LogInformation(
            "Scheduled database backups every {Hours}h, keeping {Retention}.",
            _config.IntervalHours,
            _config.RetentionCount
        );

        using PeriodicTimer timer = new(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                DatabaseBackupResult result = await backups
                    .CreateAsync("scheduled", stoppingToken)
                    .ConfigureAwait(false);

                if (!result.Success)
                {
                    logger.LogError("Scheduled database backup failed: {Reason}.", result.Message);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                // The loop is the safety net; it must outlive any single bad night.
                logger.LogError(ex, "Scheduled database backup threw.");
            }
        }
    }
}
