using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Events;

namespace Vortex.WebApi.Authentication.Tests;

/// <summary>
/// Collects what the services under test raise. The account services publish password and
/// second-factor changes for the audit trail; these tests are about the write itself, so the
/// publisher only has to exist and remember.
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
