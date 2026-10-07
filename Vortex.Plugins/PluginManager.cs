using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vortex.Logging.Extensions;
using Vortex.Plugins.Configuration;
using Vortex.Plugins.Exports;
using Vortex.Primitives.Plugins;
using Vortex.Runtime;
using Vortex.Runtime.AssemblyProcessing;

namespace Vortex.Plugins;

public sealed class PluginManager(
    IServiceProvider host,
    AssemblyProcessor processor,
    IOptions<PluginConfig> config,
    ILogger<PluginManager> logger
)
{
    private static readonly ServiceProviderOptions SP_OPTIONS = new()
    {
        ValidateScopes = true,
        ValidateOnBuild = false,
    };

    private readonly PluginConfig _config = config.Value;

    private readonly ConcurrentDictionary<string, ConcurrentBag<string>> _dependents = new(
        StringComparer.OrdinalIgnoreCase
    );

    private readonly ExportRegistry _exports = new(logger);

    /// <summary>
    ///     What a plugin publishes, as the HOST reads it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Every plugin already gets an <see cref="IPluginCatalog" /> of its own so plugins can see
    ///     each other's exports. The host had no way in at all, which made exports a plugin-to-plugin
    ///     mechanism only — and left the hotel with no way to consume anything a plugin offers except
    ///     by holding the instance, which is precisely what pins a collectible
    ///     <c>AssemblyLoadContext</c> and makes the plugin unloadable.
    ///     </para>
    ///     <para>
    ///     Through the same registry the plugins use, so a host-side consumer and a plugin-side one
    ///     see the same slot and the same swap.
    ///     </para>
    /// </remarks>
    ///     <para>
    ///     A new wrapper per call rather than a cached one: <see cref="PluginCatalog" /> holds no
    ///     state of its own, the registry behind it is the shared thing, and a property with no
    ///     backing field cannot race. DI asks once anyway.
    ///     </para>
    public IPluginCatalog Catalog => new PluginCatalog(_exports);

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _keyLocks = new(
        StringComparer.OrdinalIgnoreCase
    );

    private readonly ConcurrentDictionary<string, PluginEnvelope> _live = new(
        StringComparer.OrdinalIgnoreCase
    );

    private readonly ILogger _logger = logger;
    private readonly SemaphoreSlim _reloadGate = new(1, 1);

    /// <summary>
    /// Keys of the plugins that are fully activated right now. A key appears here only after every
    /// activation step succeeded, so this is a truthful answer to "what is actually running".
    /// </summary>
    public IReadOnlyCollection<string> GetLiveKeys() => _live.Keys.ToArray();

    private List<(PluginManifest manifest, string folder)> DiscoverPlugins()
    {
        List<(PluginManifest, string)> list = new(16);
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (string devPath in _config.DevPluginPaths)
        {
            string dir = Path.GetFullPath(devPath);

            if (!Directory.Exists(dir))
            {
                continue;
            }

            try
            {
                PluginManifest manifest = PluginHelpers.ReadManifest(dir);

                if (seen.Add(manifest.Key))
                {
                    list.Add((manifest, dir));
                    _logger.LogDebug("Discovered dev plugin {Key} from {Dir}", manifest.Key, dir);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read dev plugin manifest in {Dir}", dir);
            }
        }

        if (!Directory.Exists(_config.PluginFolderPath))
        {
            return list;
        }

        {
            foreach (string dir in Directory.EnumerateDirectories(_config.PluginFolderPath))
            {
                try
                {
                    PluginManifest manifest = PluginHelpers.ReadManifest(dir);

                    if (seen.Add(manifest.Key))
                    {
                        list.Add((manifest, dir));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to read plugin manifest in {Dir}", dir);
                }
            }
        }

        return list;
    }

    public async Task LoadAllAsync(bool unloadRemoved = true, CancellationToken ct = default)
    {
        await _reloadGate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            List<(PluginManifest manifest, string folder)> discovered = DiscoverPlugins();
            IReadOnlyList<PluginManifest> manifests = SortAndReportUnresolved(discovered);
            Dictionary<string, string> byKey = discovered.ToDictionary(
                d => d.manifest.Key,
                d => d.folder,
                StringComparer.OrdinalIgnoreCase
            );
            // Staged, not live: an envelope is only published to _live once its assembly processing
            // has also succeeded, so a plugin whose feature registration fails is never briefly
            // visible as active.
            List<PluginEnvelope> staged = new();
            ConcurrentBag<string> failedProcessing = new();
            List<Func<Task>> tasks = new();

            RebuildDependents(manifests);

            foreach (PluginManifest m in manifests)
            {
                SemaphoreSlim gate = GetKeyGate(m.Key);

                await gate.WaitAsync(ct).ConfigureAwait(false);
                string folder = byKey[m.Key];
                LoadedAssembly asm;

                try
                {
                    asm = GetLoadedPluginAssembly(m, folder);

                    // Debug, not a warning, and not actionable. Any published plugin carries a copy
                    // of every package it and the host share — EF Core, the logging abstractions,
                    // ASP.NET — and using the host's instead is the whole reason both sides mean the
                    // same type. This used to warn and to advise Private=false, which made sense
                    // while only Vortex.* assemblies were shadowed and is wrong for a NuGet package:
                    // the plugin needs those references to compile, it simply must not load them.
                    if (asm.ShadowedContractAssemblies.Count > 0)
                    {
                        _logger.LogDebug(
                            "Plugin {Key} shipped {Count} assembly(ies) the host already has; the "
                                + "host's copies were used so both sides share the types: "
                                + "{Assemblies}",
                            m.Key,
                            asm.ShadowedContractAssemblies.Count,
                            string.Join(", ", asm.ShadowedContractAssemblies)
                        );
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Failed to load assembly for {Name}@{Version} by {Author}",
                        m.Name,
                        m.Version,
                        m.Author
                    );

                    gate.Release();

                    continue;
                }

                try
                {
                    PluginEnvelope? current = _live.GetValueOrDefault(m.Key);

                    if (current is not null)
                    {
                        if (
                            _dependents.TryGetValue(m.Key, out ConcurrentBag<string>? deps)
                            && deps.Any(_live.ContainsKey)
                        )
                        {
                            throw new InvalidOperationException(
                                $"Cannot reload {m.Key} while dependents are active: {string.Join(",", deps.Where(_live.ContainsKey))}"
                            );
                        }

                        await StopAndTearDownAsync(current, ct).ConfigureAwait(false);

                        // The old envelope is torn down and its ALC unloaded. Drop it now so a
                        // failure building its replacement cannot leave a dead entry in _live —
                        // which UnloadRemovedAsync would then tear down a second time.
                        _live.TryRemove(m.Key, out _);
                    }

                    PluginEnvelope next = await BuildEnvelopeOrUnloadAsync(asm, m, folder, ct)
                        .ConfigureAwait(false);

                    staged.Add(next);

                    tasks.Add(async () =>
                    {
                        try
                        {
                            IDisposable disp = await processor
                                .ProcessAsync(asm.Assembly, next.ServiceProvider, ct)
                                .ConfigureAwait(false);

                            next.Disposables.Add(disp);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(
                                ex,
                                "Assembly processing failed for {Key}; the plugin will not be "
                                    + "activated and is being torn down.",
                                next.Manifest.Key
                            );

                            // Recorded rather than removed from `staged` directly: these callbacks
                            // run concurrently and List<T> is not thread-safe.
                            failedProcessing.Add(next.Manifest.Key);

                            await StopAndTearDownAsync(next, ct).ConfigureAwait(false);
                        }
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Failed to load {Name}@{Version} by {Author}",
                        m.Name,
                        m.Version,
                        m.Author
                    );
                }
                finally
                {
                    gate.Release();
                }
            }

            int degree = Math.Max(2, Environment.ProcessorCount * 4);

            await BoundedHelper.RunAsync(tasks, degree, ct).ConfigureAwait(false);

            // Publish only what fully activated.
            HashSet<string> failed = new(failedProcessing, StringComparer.OrdinalIgnoreCase);
            List<PluginEnvelope> activated = staged
                .Where(e => !failed.Contains(e.Manifest.Key))
                .ToList();

            foreach (PluginEnvelope env in activated)
            {
                _live[env.Manifest.Key] = env;
            }

            // "Removed" means gone from disk. A live plugin whose new build failed to load, or whose
            // reload was refused, is still on disk and keeps running.
            if (unloadRemoved)
            {
                await UnloadRemovedAsync(byKey.Keys, ct).ConfigureAwait(false);
            }

            _logger.LogInformation("Loaded {Count} plugins", _live.Count);
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    public async Task ReloadAsync(string key, CancellationToken ct = default)
    {
        await _reloadGate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            List<(PluginManifest manifest, string folder)> discovered = DiscoverPlugins();
            IReadOnlyList<PluginManifest> manifests = SortAndReportUnresolved(discovered);
            Dictionary<string, string> byKey = discovered.ToDictionary(
                d => d.manifest.Key,
                d => d.folder,
                StringComparer.OrdinalIgnoreCase
            );

            RebuildDependents(manifests);

            if (!byKey.TryGetValue(key, out string? folder))
            {
                await UnloadAsync(key, ct).ConfigureAwait(false);
                _logger.LogInformation("Plugin {Key} was removed from disk and unloaded.", key);
                return;
            }

            PluginManifest manifest =
                manifests.FirstOrDefault(m =>
                    string.Equals(m.Key, key, StringComparison.OrdinalIgnoreCase)
                )
                ?? throw new InvalidOperationException(
                    $"Cannot reload {key}; a dependency is missing or cyclic."
                );

            SemaphoreSlim gate = GetKeyGate(key);
            await gate.WaitAsync(ct).ConfigureAwait(false);

            try
            {
                foreach (
                    PluginDependency dep in manifest.Dependencies.Where(dep =>
                        !_live.ContainsKey(dep.Key)
                    )
                )
                {
                    throw new InvalidOperationException(
                        $"Cannot reload {key}; dependency {dep.Key} is not active."
                    );
                }

                if (
                    _dependents.TryGetValue(key, out ConcurrentBag<string>? deps)
                    && deps.Any(_live.ContainsKey)
                )
                {
                    throw new InvalidOperationException(
                        $"Cannot reload {key} while dependents are active: {string.Join(",", deps.Where(_live.ContainsKey))}"
                    );
                }

                LoadedAssembly asm = GetLoadedPluginAssembly(manifest, folder);
                PluginEnvelope? current = _live.GetValueOrDefault(key);

                if (current is not null)
                {
                    await StopAndTearDownAsync(current, ct).ConfigureAwait(false);

                    // Torn down and unloaded: drop it before building the replacement so a failed
                    // rebuild cannot leave a dead envelope visible in _live.
                    _live.TryRemove(key, out _);
                }

                PluginEnvelope next = await BuildEnvelopeOrUnloadAsync(asm, manifest, folder, ct)
                    .ConfigureAwait(false);

                // Assembly processing is part of the activation, so it happens before the plugin is
                // published: a plugin whose feature registration fails must never appear as live.
                try
                {
                    IDisposable disp = await processor
                        .ProcessAsync(asm.Assembly, next.ServiceProvider, ct)
                        .ConfigureAwait(false);

                    next.Disposables.Add(disp);
                }
                catch
                {
                    await StopAndTearDownAsync(next, ct).ConfigureAwait(false);

                    throw;
                }

                _live[key] = next;

                _logger.LogInformation("Reloaded plugin {Key}", key);
            }
            finally
            {
                gate.Release();
            }
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    private async Task UnloadAsync(string key, CancellationToken ct = default)
    {
        SemaphoreSlim gate = GetKeyGate(key);

        await gate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            if (
                _dependents.TryGetValue(key, out ConcurrentBag<string>? deps)
                && deps.Any(_live.ContainsKey)
            )
            {
                throw new InvalidOperationException(
                    $"Cannot unload {key}; dependents active: {string.Join(",", deps.Where(_live.ContainsKey))}"
                );
            }

            if (_live.TryRemove(key, out PluginEnvelope? env))
            {
                await StopAndTearDownAsync(env, ct).ConfigureAwait(false);

                _logger.LogInformation("Unloaded {Key}", key);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task UnloadAllAsync(CancellationToken ct = default)
    {
        HashSet<string> keys = _live.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, List<string>> dependents = new(StringComparer.OrdinalIgnoreCase);

        foreach (KeyValuePair<string, ConcurrentBag<string>> kvp in _dependents)
        {
            string dep = kvp.Key;
            List<string> list = kvp.Value.Where(keys.Contains).ToList();

            if (list.Count > 0)
            {
                dependents[dep] = list;
            }
        }

        while (keys.Count > 0)
        {
            List<string> leafs = keys.Where(k => !dependents.Values.Any(list => list.Contains(k)))
                .ToList();

            if (leafs.Count == 0)
            {
                leafs = [.. keys];
            }

            foreach (string k in leafs)
            {
                try
                {
                    await UnloadAsync(k, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to unload plugin {Key}", k);
                }

                keys.Remove(k);
            }
        }
    }

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

    private IReadOnlyList<PluginManifest> SortAndReportUnresolved(
        List<(PluginManifest manifest, string folder)> discovered
    )
    {
        IReadOnlyList<PluginManifest> sorted = PluginHelpers.SortManifests([
            .. discovered.Select(d => d.manifest),
        ]);

        foreach ((PluginManifest m, string folder) in discovered)
        {
            if (!sorted.Contains(m))
            {
                _logger.LogError(
                    "Plugin {Key} in {Folder} is skipped: a dependency is missing or cyclic",
                    m.Key,
                    folder
                );
            }
        }

        return sorted;
    }

    private async Task UnloadRemovedAsync(IEnumerable<string> keys, CancellationToken ct)
    {
        HashSet<string> keep = new(keys, StringComparer.OrdinalIgnoreCase);
        List<string> removed = _live.Keys.Where(k => !keep.Contains(k)).ToList();

        foreach (string k in removed)
        {
            try
            {
                await UnloadAsync(k, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to unload removed plugin {Key}", k);
            }
        }
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

    private SemaphoreSlim GetKeyGate(string key)
    {
        return _keyLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
    }

    private void RebuildDependents(IEnumerable<PluginManifest> manifests)
    {
        _dependents.Clear();

        foreach (PluginManifest manifest in manifests)
        {
            foreach (PluginDependency dep in manifest.Dependencies)
            {
                _dependents.GetOrAdd(dep.Key, _ => []).Add(manifest.Key);
            }
        }
    }
}
