using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vortex.Logging.Extensions;
using Vortex.Plugins.Exports;
using Vortex.Primitives.Plugins;
using Vortex.Runtime;
using Vortex.Runtime.AssemblyProcessing;

namespace Vortex.Plugins;

/// <summary>Turning an assembly on disk into a running plugin, and undoing it all if a step fails.</summary>
public sealed partial class PluginManager
{
    private static LoadedAssembly GetLoadedPluginAssembly(PluginManifest manifest, string folder)
    {
        string asmPath = PluginHelpers.GetAssemblyPath(folder, manifest);

        return AssemblyMemoryLoader.LoadFromBytes(asmPath);
    }

    /// <summary>
    /// Wraps <see cref="BuildEnvelopeAsync"/> so a failed activation is fully undone.
    /// <para>
    /// Previously this only unloaded the <c>AssemblyLoadContext</c> — while the plugin's
    /// <see cref="ServiceProvider"/>, its already-started <see cref="IHostedService"/>s and its
    /// published exports were all left in place. The rollback stack now undoes every completed step
    /// in reverse first, and the ALC is unloaded last, when nothing references it any more.
    /// </para>
    /// <para>
    /// That change was made to silence a "possible memory leak" warning, and did not, because the
    /// warning never had anything to do with the rollback: the check behind it waited for the
    /// context to be collected from a frame that was itself holding the context, so it reported a
    /// leak on every input and cost five seconds doing it. The wait is gone; whether the contexts
    /// really come back out is measured by <c>FailedActivationRetentionTests</c>, from a frame that
    /// never held one, and they do.
    /// </para>
    /// </summary>
    private async Task<PluginEnvelope> BuildEnvelopeOrUnloadAsync(
        LoadedAssembly asm,
        PluginManifest m,
        string folder,
        CancellationToken ct
    )
    {
        ActivationRollback rollback = new(_logger, m.Key);

        try
        {
            PluginEnvelope envelope = await BuildEnvelopeAsync(asm, m, folder, rollback, ct)
                .ConfigureAwait(false);

            // The envelope owns teardown from here on.
            rollback.Commit();

            return envelope;
        }
        catch
        {
            await rollback.UnwindAsync().ConfigureAwait(false);

            AssemblyMemoryLoader.Unload(asm.Alc);

            throw;
        }
    }

    private async Task<PluginEnvelope> BuildEnvelopeAsync(
        LoadedAssembly asm,
        PluginManifest m,
        string folder,
        ActivationRollback rollback,
        CancellationToken ct
    )
    {
        IVortexPlugin inst = CreatePluginInstance(asm.Assembly);

        // IVortexPlugin is IAsyncDisposable but nothing ever disposed the instance, because it is
        // Activator-created rather than DI-owned.
        rollback.Push("dispose plugin instance", () => inst.DisposeAsync().AsTask());

        if (!string.Equals(inst.Key, m.Key, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Plugin key mismatch: manifest={m.Key} entry={inst.Key}"
            );
        }

        ServiceProvider sp = CreatePluginServiceProvider(inst, m);

        rollback.Push("dispose service provider", () => sp.DisposeAsync().AsTask());

        ExportJournal journal = new(_logger);

        rollback.Push("restore previous exports", () => journal.RollbackAsync(m.Key));

        await inst.BindExportsAsync(new ExportBinder(_exports, journal), sp).ConfigureAwait(false);
        await StartPluginAsync(inst, sp, rollback, ct).ConfigureAwait(false);

        // Every step succeeded, so the instances this activation displaced are genuinely dead.
        await journal.CommitAsync().ConfigureAwait(false);

        return new PluginEnvelope
        {
            Key = m.Key,
            Assembly = asm.Assembly,
            Alc = asm.Alc,
            Manifest = m,
            Folder = folder,
            Instance = inst,
            ServiceProvider = sp,
            Disposables = [sp],
        };
    }

    private static IVortexPlugin CreatePluginInstance(Assembly asm)
    {
        Type pluginType =
            AssemblyExplorer.FindType(asm, typeof(IVortexPlugin))
            ?? throw new InvalidOperationException(
                $"Failed to find IVortexPlugin in assembly '{asm.GetName().Name}'."
            );

        return (IVortexPlugin)Activator.CreateInstance(pluginType)!;
    }

    private ServiceProvider CreatePluginServiceProvider(
        IVortexPlugin plugin,
        PluginManifest manifest
    )
    {
        ServiceCollection services = new();

        services.AddSingleton(manifest);
        services.AddSingleton<IPluginCatalog>(new PluginCatalog(_exports));
        services.AddSingleton<IHostServices>(new HostServices(host));
        services.ConfigurePrefixedLogging(host, manifest.Name);

        plugin.ConfigureServices(services, manifest);

        ServiceProvider provider = services.BuildServiceProvider(SP_OPTIONS);

        VerifyHostServices(provider, manifest);

        return provider;
    }

    /// <summary>
    /// Checks every host service the plugin declared with <c>UseHostService&lt;T&gt;</c> is actually
    /// there.
    /// </summary>
    /// <remarks>
    /// The registration those calls produce is a factory, so without this a service the host does
    /// not have fails at the moment it is first resolved. For anything request-driven — a plugin
    /// serving HTTP, say — that is the first request that happens to need it, long after the load
    /// that should have refused. Failing here instead makes it an activation error, which the
    /// rollback then unwinds like any other.
    /// </remarks>
    private void VerifyHostServices(IServiceProvider plugin, PluginManifest manifest)
    {
        List<string> missing = [];

        foreach (
            HostServiceExtensions.HostServiceRequirement requirement in plugin.GetServices<HostServiceExtensions.HostServiceRequirement>()
        )
        {
            if (host.GetService(requirement.ServiceType) is null)
            {
                missing.Add(requirement.ServiceType.FullName ?? requirement.ServiceType.Name);
            }
        }

        if (missing.Count == 0)
        {
            return;
        }

        _logger.LogError(
            "Plugin {Key} borrows host services this host does not provide: {Missing}",
            manifest.Key,
            string.Join(", ", missing)
        );

        throw new InvalidOperationException(
            $"Plugin '{manifest.Key}' requires host service(s) not registered: {string.Join(", ", missing)}."
        );
    }

    private async Task StartPluginAsync(
        IVortexPlugin plugin,
        IServiceProvider sp,
        ActivationRollback rollback,
        CancellationToken ct
    )
    {
        await ProcessMigrationsAsync(sp, ct).ConfigureAwait(false);

        // Migrations are deliberately not compensated. IPluginDbModule exposes UninstallAsync, but
        // that drops the plugin's tables — running it because a hosted service failed to start would
        // turn a recoverable startup error into data loss. A partially applied migration is EF's own
        // per-migration transaction to resolve, and the plugin will retry on the next load.
        foreach (IHostedService svc in sp.GetServices<IHostedService>())
        {
            try
            {
                await svc.StartAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Hosted service {Service} failed to start for plugin {Plugin}",
                    svc.GetType().FullName,
                    plugin.GetType().FullName
                );

                throw;
            }

            // Registered only after a successful start, so the rollback stops exactly the services
            // that are actually running. The previous version rethrew on the first failure and left
            // every earlier service in the loop started.
            IHostedService started = svc;

            rollback.Push(
                $"stop hosted service {started.GetType().Name}",
                () => started.StopAsync(CancellationToken.None)
            );
        }

        await plugin.StartAsync(sp, ct).ConfigureAwait(false);

        rollback.Push("stop plugin", () => plugin.StopAsync(CancellationToken.None));
    }

    private static async Task ProcessMigrationsAsync(
        IServiceProvider pluginRoot,
        CancellationToken ct
    )
    {
        AsyncServiceScope scope = pluginRoot.CreateAsyncScope();

        try
        {
            IServiceProvider sp = scope.ServiceProvider;
            IPluginDbModule? dbModule = sp.GetService<IPluginDbModule>();

            if (dbModule is null)
            {
                return;
            }

            await dbModule.MigrateAsync(sp, ct).ConfigureAwait(false);
        }
        finally
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }
}
