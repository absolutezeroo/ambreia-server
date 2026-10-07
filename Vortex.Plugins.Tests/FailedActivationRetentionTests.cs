using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vortex.Plugins.Configuration;
using Vortex.Runtime.AssemblyProcessing;
using Xunit;

namespace Vortex.Plugins.Tests;

/// <summary>
///     After an activation fails and its rollback has run, is anything still pinning the plugin's
///     load context?
/// </summary>
/// <remarks>
///     <para>
///     Asked from a frame that never held the context: run the failed activation, let every frame
///     involved go away, and only then drive the collection. Waiting from the frame that ran the
///     load could only ever answer "still alive".
///     </para>
///     <para>
///     Only the context created by this load is watched. Other tests in the collection load the
///     same plugin and never unload it, so "the first plugin context alive" is usually theirs, and
///     waiting for it to be collected fails after a ten-second spin per row.
///     </para>
/// </remarks>
[Collection(TestPluginCollection.NAME)]
public sealed class FailedActivationRetentionTests : IDisposable
{
    private const string PLUGIN_KEY = "vortex-test-plugin";
    private const string FAILURE_VARIABLE = "VORTEX_TEST_PLUGIN_FAILURE";
    private const string TRACE_VARIABLE = "VORTEX_TEST_PLUGIN_TRACE";
    private const string ASSEMBLY_FILE = "Vortex.Plugins.TestPlugin.dll";

    private readonly string _pluginFolder;

    public FailedActivationRetentionTests()
    {
        _pluginFolder = CopyTestPluginToTempFolder();
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(FAILURE_VARIABLE, null);
        Environment.SetEnvironmentVariable(TRACE_VARIABLE, null);

        try
        {
            Directory.Delete(_pluginFolder, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder is not worth failing a test over.
        }
    }

    [Theory]
    [InlineData("Migration")]
    [InlineData("BindExports")]
    [InlineData("HostedServiceStart")]
    [InlineData("PluginStart")]
    public async Task AFailedActivation_leavesNothingPinningThePluginContext(string failurePoint)
    {
        Environment.SetEnvironmentVariable(FAILURE_VARIABLE, failurePoint);

        HashSet<AssemblyLoadContext> before = PluginContexts();

        await RunFailedLoadAsync();

        WeakReference? context = FindNewPluginContext(before);
        before.Clear();

        // Already gone from AssemblyLoadContext.All is the strongest possible pass: the runtime
        // finished the unload before anything even looked.
        bool collected = context is null || Collected(context);

        collected
            .Should()
            .BeTrue(
                $"an activation that failed at {failurePoint} rolled everything back, so once the "
                    + "frames that ran it are gone nothing should still reach its types"
            );
    }

    /// <summary>
    ///     Runs one failing load and keeps nothing. Not inlined, so the manager and everything it
    ///     built live in a frame that is gone by the time the caller looks.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private async Task RunFailedLoadAsync()
    {
        PluginManager manager = CreateManager();

        await manager.LoadAllAsync(unloadRemoved: false, CancellationToken.None);

        manager.GetLiveKeys().Should().NotContain(PLUGIN_KEY, "the activation was made to fail");
    }

    /// <summary>
    ///     A weak reference to the plugin's collectible context, or null if it is already collected.
    /// </summary>
    /// <remarks>
    ///     Not inlined, and returns nothing but the weak reference: the enumeration below puts a
    ///     strong reference to every live context on this frame, which is exactly what must not
    ///     survive into the polling loop.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference? FindNewPluginContext(HashSet<AssemblyLoadContext> before)
    {
        AssemblyLoadContext? plugin = AssemblyLoadContext.All.FirstOrDefault(c =>
            c is ByteLoadingAlc && !before.Contains(c)
        );

        return plugin is null ? null : new WeakReference(plugin);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static HashSet<AssemblyLoadContext> PluginContexts() =>
        [.. AssemblyLoadContext.All.Where(c => c is ByteLoadingAlc)];

    /// <summary>
    ///     Drives the collection from a frame that never held the context. Three collections per
    ///     round because an unload finishes across more than one.
    /// </summary>
    private static bool Collected(WeakReference context, int maxMs = 10000)
    {
        Stopwatch clock = Stopwatch.StartNew();

        while (clock.ElapsedMilliseconds < maxMs)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            if (!context.IsAlive)
            {
                return true;
            }
        }

        return !context.IsAlive;
    }

    private PluginManager CreateManager()
    {
        ServiceCollection hostServices = new();

        PluginConfig config = new()
        {
            PluginFolderPath = Path.Combine(Path.GetTempPath(), $"vortex-empty-{Guid.NewGuid():N}"),
            DevPluginPaths = [_pluginFolder],
            HotReloadEnabled = false,
        };

        return new PluginManager(
            hostServices.BuildServiceProvider(),
            new AssemblyProcessor([]),
            Options.Create(config),
            NullLogger<PluginManager>.Instance
        );
    }

    private static string CopyTestPluginToTempFolder()
    {
        string destination = Path.Combine(
            Path.GetTempPath(),
            $"vortex-retention-plugin-{Guid.NewGuid():N}"
        );

        Directory.CreateDirectory(destination);

        File.Copy(
            Path.Combine(AppContext.BaseDirectory, ASSEMBLY_FILE),
            Path.Combine(destination, ASSEMBLY_FILE),
            overwrite: true
        );

        File.WriteAllText(
            Path.Combine(destination, "manifest.json"),
            JsonSerializer.Serialize(
                new
                {
                    Name = "Vortex Test Plugin",
                    Key = PLUGIN_KEY,
                    Version = "1.0.0",
                    Author = "Vortex",
                    AssemblyFile = ASSEMBLY_FILE,
                }
            )
        );

        return destination;
    }
}
