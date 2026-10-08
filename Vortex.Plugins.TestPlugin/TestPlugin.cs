using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vortex.Primitives.Hosting;
using Vortex.Primitives.Plugins;
using Vortex.Primitives.Plugins.Exports;

namespace Vortex.Plugins.TestPlugin;

/// <summary>
/// The one <see cref="IVortexPlugin" /> in this assembly. It fails at whichever activation step
/// <see cref="FailureSwitch.Current" /> names, and traces the steps it reaches so the tests can
/// assert that a rollback stopped exactly what had been started.
/// </summary>
public sealed class TestPlugin : IVortexPlugin
{
    public const string KEY = "vortex-test-plugin";

    public string Key => KEY;

    public string Version => "1.0.0";

    public void ConfigureServices(IServiceCollection services, PluginManifest manifest)
    {
        services.AddSingleton<TrackedResource>();
        services.AddSingleton<IPluginDbModule, TestDbModule>();

        // Borrowing a host service, which is the thing a plugin of any size has to be able to do:
        // its container starts empty, so without this it cannot constructor-inject anything the
        // hotel owns. IReferenceDataReloader is used because it lives in Vortex.Primitives, which
        // the ALC shares with the host — so both sides mean the same type, which is the whole
        // point.
        if (FailureSwitch.BorrowsHostService)
        {
            services.UseHostService<IReferenceDataReloader>();
        }

        // Two hosted services: the tests need "the first started, the second failed" to prove the
        // rollback stops the first one rather than leaving it running.
        services.AddSingleton<IHostedService>(
            new TrackedHostedService("first", failOnStart: false)
        );
        services.AddSingleton<IHostedService>(
            new TrackedHostedService(
                "second",
                failOnStart: FailureSwitch.Current == FailurePoint.HostedServiceStart
            )
        );
    }

    public Task BindExportsAsync(IExportBinder binder, IServiceProvider services)
    {
        FailureSwitch.Trace("bind-exports");

        // Resolved (not merely registered) so the container actually constructs it: a
        // ServiceProvider only disposes the singletons it created, so an unresolved registration
        // would make "was the container disposed?" unobservable. This is the first activation step,
        // so every failure point downstream has a live singleton to account for.
        _ = services.GetRequiredService<TrackedResource>();

        if (FailureSwitch.Current == FailurePoint.BindExports)
        {
            throw new InvalidOperationException("Export binding failed on purpose.");
        }

        return binder.ExportAsync<ITestGreeter>("greeter", new PluginGreeter());
    }

    public Task StartAsync(IServiceProvider services, CancellationToken ct)
    {
        FailureSwitch.Trace("plugin-start");

        // Traced rather than asserted here: the plugin's statics are its own copy, so the trace
        // environment variable is the only channel the test on the other side of the ALC can read.
        if (FailureSwitch.BorrowsHostService)
        {
            FailureSwitch.Trace(
                services.GetService<IReferenceDataReloader>() is null
                    ? "host-service-missing"
                    : "host-service-resolved"
            );
        }

        if (FailureSwitch.Current == FailurePoint.PluginStart)
        {
            throw new InvalidOperationException("Plugin start failed on purpose.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        FailureSwitch.Trace("plugin-stop");

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        FailureSwitch.Trace("plugin-dispose");

        return ValueTask.CompletedTask;
    }

    private sealed class PluginGreeter : ITestGreeter
    {
        public string Greet() => "from-plugin";
    }
}
