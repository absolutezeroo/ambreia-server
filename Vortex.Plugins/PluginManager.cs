using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vortex.Plugins.Configuration;
using Vortex.Plugins.Exports;
using Vortex.Primitives.Plugins;
using Vortex.Runtime;
using Vortex.Runtime.AssemblyProcessing;

namespace Vortex.Plugins;

/// <summary>
/// Loads, reloads and unloads the plugins, one key at a time. Its parts: what is on disk
/// (<c>PluginManager.Discovery.cs</c>), turning an assembly into a running plugin
/// (<c>.Activation.cs</c>) and taking one apart (<c>.Teardown.cs</c>).
/// </summary>
public sealed partial class PluginManager(
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
    ///     <para>
    ///     A new wrapper per call rather than a cached one: <see cref="PluginCatalog" /> holds no
    ///     state of its own, the registry behind it is the shared thing, and a property with no
    ///     backing field cannot race. DI asks once anyway.
    ///     </para>
    /// </remarks>
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

    public async Task LoadAllAsync(bool unloadRemoved = true, CancellationToken ct = default)
    {
        await _reloadGate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            (IReadOnlyList<PluginManifest> manifests, Dictionary<string, string> byKey) =
                Discover();
            // Staged, not live: an envelope is only published to _live once its assembly processing
            // has also succeeded, so a plugin whose feature registration fails is never briefly
            // visible as active.
            List<PluginEnvelope> staged = new();
            ConcurrentBag<string> failedProcessing = new();
            List<Func<Task>> tasks = new();

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
            (IReadOnlyList<PluginManifest> manifests, Dictionary<string, string> byKey) =
                Discover();

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

    private SemaphoreSlim GetKeyGate(string key)
    {
        return _keyLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
    }
}
