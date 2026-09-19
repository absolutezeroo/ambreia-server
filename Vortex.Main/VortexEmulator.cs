using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Runtime;
using Vortex.Main.Configuration;
using Vortex.Main.Hosting;
using Vortex.Primitives.Hosting;
using Vortex.Primitives.Networking;

namespace Vortex.Main;

public class VortexEmulator(
    ILogger<VortexEmulator> logger,
    IEnumerable<IReferenceDataProvider> referenceDataProviders,
    INetworkManager networkManager,
    IGrainFactory grainFactory,
    IOptions<OrleansHostConfig> orleansConfig,
    IMaintenanceAnnouncer announcer,
    IOptions<ShutdownConfig> shutdownConfig
) : IHostedLifecycleService
{
    private readonly ILogger<VortexEmulator> _logger = logger;
    private readonly IEnumerable<IReferenceDataProvider> _referenceDataProviders =
        referenceDataProviders;
    private readonly INetworkManager _networkManager = networkManager;
    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly OrleansHostConfig _orleansConfig = orleansConfig.Value;
    private readonly IMaintenanceAnnouncer _announcer = announcer;
    private readonly ShutdownConfig _shutdownConfig = shutdownConfig.Value;

    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            await RefuseAnUndeclaredSecondSiloAsync(ct).ConfigureAwait(false);
            WarnAboutTheShutdownNotice();

            IEnumerable<IGrouping<int, IReferenceDataProvider>> stages = _referenceDataProviders
                .GroupBy(p => p.LoadStage)
                .OrderBy(stage => stage.Key);

            foreach (IGrouping<int, IReferenceDataProvider> stage in stages)
            {
                await Task.WhenAll(stage.Select(p => p.ReloadAsync(ct))).ConfigureAwait(false);
            }

            await _networkManager.StartAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Emulator startup was cancelled.");

            throw;
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Emulator failed to start!");

            throw;
        }
    }

    /// <summary>
    ///     The clustering configuration can form a multi-silo cluster; the application semantics
    ///     cannot yet (see <see cref="OrleansHostConfig.MultiSiloReady"/> for what exactly goes
    ///     stale). Nothing about that failure mode is loud — the hotel keeps serving, with one node
    ///     handing out furniture definitions an admin replaced hours ago — so the second silo is
    ///     stopped here instead, where it is a startup error somebody reads.
    /// </summary>
    private async Task RefuseAnUndeclaredSecondSiloAsync(CancellationToken ct)
    {
        if (_orleansConfig.MultiSiloReady)
        {
            return;
        }

        Dictionary<SiloAddress, SiloStatus> hosts = await _grainFactory
            .GetGrain<IManagementGrain>(0)
            .GetHosts(onlyActive: true)
            .WaitAsync(ct)
            .ConfigureAwait(false);

        if (hosts.Count <= 1)
        {
            return;
        }

        throw new InvalidOperationException(
            $"{hosts.Count} active silos are in this cluster, and this build's caches, metrics, "
                + "room streams and messenger presence fan-out are silo-local: an admin catalog or "
                + "furniture edit would reload one node and leave the others serving stale "
                + "definitions indefinitely, and a friend on another silo would never be told you "
                + "came online. Make those "
                + "components cluster-aware, then set "
                + $"'{OrleansHostConfig.SECTION_NAME}:{nameof(OrleansHostConfig.MultiSiloReady)}' "
                + $"to true. Active silos: {string.Join(", ", hosts.Keys)}."
        );
    }

    /// <summary>
    ///     Says out loud, once per boot, what the shutdown notice silently depends on.
    /// </summary>
    /// <remarks>
    ///     The notice is a delay this process takes inside its own shutdown, so it is only honoured
    ///     for as long as whoever stopped the container is willing to wait. Coolify stops one with
    ///     <c>docker stop --time=30</c> and Docker Compose with the service's
    ///     <c>stop_grace_period</c> (10s unless told otherwise); either expiring mid-notice is a
    ///     SIGKILL, which means no room persistence flush and no Orleans drain — on every deploy,
    ///     quietly, because nothing in a green log says the hotel was killed rather than stopped.
    /// </remarks>
    private void WarnAboutTheShutdownNotice()
    {
        if (_shutdownConfig.NoticeMinutes <= 0)
        {
            return;
        }

        _logger.LogWarning(
            "Players get {Minutes} minute(s) of notice before a shutdown, so this process will "
                + "delay its own stop by that long. Whatever stops the container must allow at "
                + "least {Seconds}s (docker stop --time / compose stop_grace_period) or it will be "
                + "killed mid-notice, with no persistence flush.",
            _shutdownConfig.NoticeMinutes,
            (_shutdownConfig.NoticeMinutes * 60) + 30
        );
    }

    public Task StartingAsync(CancellationToken ct) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    ///     Warns everyone online, then holds the shutdown open for as long as it promised.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     In <c>StoppingAsync</c> and not <c>StopAsync</c> because the host runs every
    ///     <c>StoppingAsync</c> before it stops a single hosted service: the sockets are still up,
    ///     Orleans is still up, and the warning can actually reach a player. By <c>StopAsync</c> the
    ///     teardown is already under way and the order between services would decide whether it
    ///     arrived.
    ///     </para>
    ///     <para>
    ///     An empty hotel is not held. Nobody to warn means nothing to wait for, and a deploy at
    ///     four in the morning should not cost five minutes for an audience of zero.
    ///     </para>
    /// </remarks>
    public async Task StoppingAsync(CancellationToken ct)
    {
        int notice = _shutdownConfig.NoticeMinutes;

        if (notice <= 0)
        {
            return;
        }

        try
        {
            int reached = await _announcer
                .AnnounceAsync(notice, _shutdownConfig.DowntimeMinutes, ct)
                .ConfigureAwait(false);

            if (reached == 0)
            {
                _logger.LogInformation("Nobody is connected; shutting down without waiting.");

                return;
            }

            _logger.LogInformation(
                "Told {Reached} player(s) the hotel restarts in {Minutes} minute(s); holding the "
                    + "shutdown until then.",
                reached,
                notice
            );

            // The last minute gets its own warning, because the first one was up to five minutes
            // ago and the client's alert does not tick. Everything in between stays silent: each
            // send is another popup, not a number changing.
            if (notice > 1)
            {
                await Task.Delay(TimeSpan.FromMinutes(notice - 1), ct).ConfigureAwait(false);
                await _announcer
                    .AnnounceAsync(1, _shutdownConfig.DowntimeMinutes, ct)
                    .ConfigureAwait(false);
            }

            await Task.Delay(TimeSpan.FromMinutes(1), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The host's own shutdown timeout, or a grace period shorter than the notice. The
            // shutdown carries on either way — this is a courtesy, not a barrier.
            _logger.LogWarning(
                "The shutdown notice was cut short; players were told {Minutes} minute(s) and did "
                    + "not get them.",
                notice
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The shutdown notice failed; shutting down anyway.");
        }
    }

    public Task StoppedAsync(CancellationToken ct) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Vortex StopAsync called.");

        try
        {
            await _networkManager.StopAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop the network manager during shutdown.");
        }
    }
}
