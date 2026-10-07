using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vortex.Observability.Diagnostics;
using Vortex.Primitives.Moderation.WordFilter;

namespace Vortex.Permissions.WordFilter;

/// <summary>
/// Runs the filter's first reload once the host — and with it the silo — has started. Loading in
/// <see cref="StartAsync"/> would race the silo for the config grain.
/// </summary>
/// <remarks>
/// Retried, doubling from a second: after a restart MySQL is often still waking up, and a single
/// failed attempt would leave the hotel unfiltered until somebody happened to edit the list.
/// </remarks>
internal sealed class WordFilterLoader(
    IWordFilter filter,
    IHostApplicationLifetime lifetime,
    ILogger<WordFilterLoader> logger
) : IHostedService
{
    private const int Attempts = 6;

    private static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(1);

    public Task StartAsync(CancellationToken ct)
    {
        lifetime.ApplicationStarted.Register(() =>
            _ = LoadWithRetryAsync(
                filter,
                logger,
                Attempts,
                FirstDelay,
                lifetime.ApplicationStopping
            )
        );

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Reloads until one attempt succeeds or <paramref name="attempts"/> have failed, waiting
    /// <paramref name="firstDelay"/> after the first failure and twice as long after each next one.
    /// Never throws: the caller is a fire-and-forget startup hook, so the outcome is the log and the
    /// returned flag.
    /// </summary>
    internal static async Task<bool> LoadWithRetryAsync(
        IWordFilter filter,
        ILogger logger,
        int attempts,
        TimeSpan firstDelay,
        CancellationToken ct
    )
    {
        TimeSpan delay = firstDelay;

        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                await filter.ReloadAsync(ct).ConfigureAwait(false);

                return true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception ex)
            {
                bool last = attempt == attempts;

                logger.LogError(
                    VortexEventIds.WordFilterLoadFailed,
                    ex,
                    last
                        ? "Word filter failed its first load {Attempt} times; it passes everything until the next edit of the list"
                        : "Word filter failed its first load (attempt {Attempt}); retrying",
                    attempt
                );

                if (last)
                {
                    return false;
                }
            }

            try
            {
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return false;
            }

            delay += delay;
        }

        return false;
    }
}
