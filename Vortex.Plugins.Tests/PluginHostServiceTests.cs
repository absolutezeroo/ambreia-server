using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vortex.Plugins.Configuration;
using Vortex.Plugins.TestPlugin;
using Vortex.Primitives.Hosting;
using Vortex.Runtime.AssemblyProcessing;
using Xunit;

namespace Vortex.Plugins.Tests;

/// <summary>
/// Whether a plugin can use the hotel it is loaded into.
/// </summary>
/// <remarks>
/// <para>
/// A plugin's container is built empty on purpose, but that made anything substantial impossible to
/// package as one: a plugin like the web API constructor-injects services the host owns, and
/// rewriting every constructor to reach through <c>IHostServices</c> by hand is not a boundary, only
/// a tax. <c>UseHostService&lt;T&gt;</c> is how a plugin borrows one and then injects it normally.
/// </para>
/// <para>
/// The second test is the reason the borrowing is declared rather than implicit. The registration is
/// a factory, so a service the host does not have would otherwise fail whenever it is first
/// resolved — for a plugin serving HTTP, the first request that happens to need it, long after the
/// load that should have refused.
/// </para>
/// </remarks>
[Collection(TestPluginCollection.NAME)]
public sealed class PluginHostServiceTests : IDisposable
{
    private const string PLUGIN_KEY = "vortex-test-plugin";

    private readonly string _pluginFolder;

    public PluginHostServiceTests()
    {
        _pluginFolder = CopyTestPluginToTempFolder();
        Environment.SetEnvironmentVariable(FailureSwitch.BORROW_VARIABLE, "1");
        Environment.SetEnvironmentVariable(FailureSwitch.TRACE_VARIABLE, null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(FailureSwitch.BORROW_VARIABLE, null);
        Environment.SetEnvironmentVariable(FailureSwitch.TRACE_VARIABLE, null);

        try
        {
            Directory.Delete(_pluginFolder, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder is not worth failing a test over.
        }
    }

    [Fact]
    public async Task A_plugin_resolves_a_host_service_it_declared()
    {
        PluginManager manager = CreateManager(withReloader: true);

        await manager.LoadAllAsync(unloadRemoved: false, CancellationToken.None);

        manager.GetLiveKeys().Should().Contain(PLUGIN_KEY);
        Trace()
            .Should()
            .Contain(
                "host-service-resolved",
                "the plugin injected the hotel's own service, across the load context boundary"
            );
    }

    [Fact]
    public async Task A_plugin_borrowing_a_service_the_host_lacks_is_refused_at_activation()
    {
        PluginManager manager = CreateManager(withReloader: false);

        await manager.LoadAllAsync(unloadRemoved: false, CancellationToken.None);

        manager
            .GetLiveKeys()
            .Should()
            .BeEmpty("a plugin that cannot get what it declared must not be left running");

        // And it never reached its own start: the check runs while the container is being built,
        // which is what makes this an activation error the rollback can unwind.
        Trace().Should().NotContain("plugin-start");
    }

    // ── Harness ──────────────────────────────────────────────────────────────

    /// <summary>A host service to borrow. Only its presence in the host provider is under test.</summary>
    private sealed class StubReloader : IReferenceDataReloader
    {
        public IReadOnlyList<string> Providers => [];

        public Task<ReloadOutcome> ReloadAsync(string provider, CancellationToken ct) =>
            Task.FromResult(new ReloadOutcome(provider, Reloaded: true, ElapsedMs: 0));
    }

    private PluginManager CreateManager(bool withReloader)
    {
        ServiceCollection hostServices = new();

        if (withReloader)
        {
            hostServices.AddSingleton<IReferenceDataReloader, StubReloader>();
        }

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

    private static List<string> Trace() =>
        (Environment.GetEnvironmentVariable(FailureSwitch.TRACE_VARIABLE) ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .ToList();

    /// <summary>
    /// The plugin's own DLL and a manifest, and nothing else — shipping Vortex.Primitives.dll
    /// alongside it would give the plugin its own copy of every contract type.
    /// </summary>
    private static string CopyTestPluginToTempFolder()
    {
        const string ASSEMBLY_FILE = "Vortex.Plugins.TestPlugin.dll";

        string destination = Path.Combine(
            Path.GetTempPath(),
            $"vortex-host-service-plugin-{Guid.NewGuid():N}"
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
