using System;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Plugins;

namespace Vortex.Plugins.TestPlugin;

/// <summary>Runs before any hosted service, so a migration failure aborts the activation early.</summary>
public sealed class TestDbModule : IPluginDbModule
{
    public Task MigrateAsync(IServiceProvider sp, CancellationToken ct)
    {
        FailureSwitch.Trace("migrate");

        if (FailureSwitch.Current == FailurePoint.Migration)
        {
            throw new InvalidOperationException("Migration failed on purpose.");
        }

        return Task.CompletedTask;
    }

    public Task UninstallAsync(IServiceProvider sp, CancellationToken ct) => Task.CompletedTask;
}
