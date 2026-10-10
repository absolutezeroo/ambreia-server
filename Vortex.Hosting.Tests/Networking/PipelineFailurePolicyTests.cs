using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Vortex.Events.Registry;
using Vortex.Messages.Registry;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Orleans;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Hosting.Tests.Networking;

/// <summary>
/// The two pipelines read the same option and must answer it differently: a command behavior is a
/// gate, so a gate that throws refuses the packet; an event behavior is an optional hook, so one
/// that throws must not stop the publication.
/// </summary>
public sealed class PipelineFailurePolicyTests
{
    [Fact]
    public void TheCommandPipeline_FailsClosed()
    {
        using ServiceProvider sp = new ServiceCollection().BuildServiceProvider();
        MessageRegistry registry = new(
            sp,
            FakeProxy.Create<IErrorGroupingSink>(_ => null),
            FakeProxy.Create<IVortexContextAccessor>(_ => null),
            NullLogger<MessageRegistry>.Instance
        );

        registry
            .BehaviorErrorsFailClosed.Should()
            .BeTrue("a rate limit or phase gate that throws has not said yes");
    }

    [Fact]
    public void TheEventPipeline_StaysOpen()
    {
        using ServiceProvider sp = new ServiceCollection().BuildServiceProvider();
        EventRegistry registry = new(
            sp,
            FakeProxy.Create<IErrorGroupingSink>(_ => null),
            FakeProxy.Create<IVortexContextAccessor>(_ => null),
            NullLogger<EventRegistry>.Instance
        );

        registry
            .BehaviorErrorsFailClosed.Should()
            .BeFalse("an optional hook that throws must not silence the event");
    }
}
