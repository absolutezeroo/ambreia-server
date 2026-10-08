using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Vortex.Plugins.TestPlugin;

/// <summary>Traces its own start/stop so rollback ordering is observable.</summary>
public sealed class TrackedHostedService(string name, bool failOnStart) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (failOnStart)
        {
            throw new InvalidOperationException($"Hosted service '{name}' failed on purpose.");
        }

        FailureSwitch.Trace($"start-{name}");

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        FailureSwitch.Trace($"stop-{name}");

        return Task.CompletedTask;
    }
}
