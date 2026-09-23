using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vortex.Observability.Diagnostics;
using Vortex.Primitives.Moderation.WordFilter;

namespace Vortex.Observability.WordFilter;

/// <summary>
/// Runs the filter's first reload once the host — and with it the silo — has started. Loading in
/// <see cref="StartAsync"/> would race the silo for the config grain.
/// </summary>
internal sealed class WordFilterLoader(
    IWordFilter filter,
    IHostApplicationLifetime lifetime,
    ILogger<WordFilterLoader> logger
) : IHostedService
{
    public Task StartAsync(CancellationToken ct)
    {
        lifetime.ApplicationStarted.Register(() =>
            _ = filter
                .ReloadAsync(lifetime.ApplicationStopping)
                .ContinueWith(
                    t =>
                        logger.LogError(
                            VortexEventIds.WordFilterLoadFailed,
                            t.Exception,
                            "Word filter failed its first load; it passes everything until the next edit of the list"
                        ),
                    TaskContinuationOptions.OnlyOnFaulted
                )
        );

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
