using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vortex.Runtime.AssemblyProcessing;

namespace Vortex.Plugins;

/// <summary>Taking a running plugin apart, every stage even when an earlier one failed.</summary>
public sealed partial class PluginManager
{
    /// <summary>
    /// Tears an activation down in reverse order: hosted services, the plugin, its registered
    /// disposables (the service provider last of those), the plugin instance, then the ALC.
    /// <para>
    /// Each stage is independently guarded so one failure cannot skip the rest — the previous
    /// version wrapped the whole sequence in a single try/catch, so a throw while enumerating hosted
    /// services meant the service provider was never disposed and the ALC could never unload.
    /// </para>
    /// </summary>
    private async Task StopAndTearDownAsync(PluginEnvelope env, CancellationToken ct)
    {
        // CancellationToken.None for the stop calls: teardown is cleanup, and abandoning it because
        // the host is already shutting down is how sockets and file handles survive the process.
        try
        {
            if (env.ServiceProvider is { } sp)
            {
                foreach (IHostedService svc in sp.GetServices<IHostedService>())
                {
                    try
                    {
                        await svc.StopAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "Hosted service {Service} failed to stop for plugin {Key}",
                            svc.GetType().FullName,
                            env.Manifest.Key
                        );
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not enumerate hosted services while tearing down plugin {Key}; continuing "
                    + "with the rest of the teardown",
                env.Manifest.Key
            );
        }

        try
        {
            await env.Instance.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to stop plugin {Key}", env.Manifest.Key);
        }

        foreach (object inst in env.Disposables)
        {
            try
            {
                switch (inst)
                {
                    case IAsyncDisposable iad:
                        await iad.DisposeAsync().ConfigureAwait(false);

                        continue;
                    case IDisposable d:
                        d.Dispose();

                        continue;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to dispose {Type} for {Key}",
                    inst.GetType().Name,
                    env.Manifest.Key
                );
            }
        }

        env.Disposables.Clear();

        try
        {
            // IVortexPlugin is IAsyncDisposable, but the instance is Activator-created rather than
            // DI-owned, so nothing was disposing it. Anything it holds kept the ALC alive.
            await env.Instance.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dispose plugin instance {Key}", env.Manifest.Key);
        }

        if (env.Alc is not null)
        {
            AssemblyMemoryLoader.Unload(env.Alc);
        }
    }
}
