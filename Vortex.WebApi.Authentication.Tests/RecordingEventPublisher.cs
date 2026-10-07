using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Events;

namespace Vortex.WebApi.Authentication.Tests;

/// <summary>
/// Collects what the services under test raise: the publisher only has to exist and remember. No
/// test in this project uses it at the moment.
/// </summary>
internal sealed class RecordingEventPublisher : IEventPublisher
{
    public List<IEvent> Published { get; } = [];

    public Task PublishAsync(IEvent @event, CancellationToken ct = default)
    {
        Published.Add(@event);

        return Task.CompletedTask;
    }
}
