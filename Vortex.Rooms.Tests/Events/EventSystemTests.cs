using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Rooms.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Events;

public sealed class EventSystemTests
{
    private const string CORRELATION_ID = "phase2-correlation";

    [Fact]
    public async Task PublishCancellableAsync_ReturnsCancelledContext_AndSkipsHandlers()
    {
        EventTestHarness harness = new EventTestHarness(CORRELATION_ID);
        CancellingProbeBehavior behavior = new CancellingProbeBehavior();
        CountingProbeHandler handler = new CountingProbeHandler();
        harness.RegisterBehavior<ProbeEvent>(behavior);
        harness.RegisterHandler<ProbeEvent>(handler);

        EventContext context = await harness
            .System.PublishCancellableAsync(
                new ProbeEvent(1, "Blocked", 10, 10),
                CancellationToken.None
            )
            .ConfigureAwait(true);

        context.Cancel.Should().BeTrue();
        context.CancelReason.Should().Be("blocked");
        context.CorrelationId.Should().Be(CORRELATION_ID);
        behavior.CorrelationId.Should().Be(CORRELATION_ID);
        handler.Count.Should().Be(0);
    }

    [Fact]
    public async Task ThrowingBehavior_IsReported_AndDoesNotCancelOrSkipHandlers()
    {
        EventTestHarness harness = new EventTestHarness(CORRELATION_ID);
        CountingProbeHandler handler = new CountingProbeHandler();
        harness.RegisterBehavior<ProbeEvent>(new ThrowingProbeBehavior());
        harness.RegisterHandler<ProbeEvent>(handler);

        EventContext context = await harness
            .System.PublishCancellableAsync(
                new ProbeEvent(1, "Allowed", 10, 10),
                CancellationToken.None
            )
            .ConfigureAwait(true);

        context.Cancel.Should().BeFalse();
        handler.Count.Should().Be(1);
        harness.ErrorSink.Count.Should().Be(1);
        harness.ErrorSink.LastSource.Should().Be("event-registry.behavior-invoke");
    }

    private sealed class CancellingProbeBehavior : IEventBehavior<ProbeEvent>
    {
        public string? CorrelationId { get; private set; }

        public async ValueTask InvokeAsync(
            ProbeEvent env,
            EventContext ctx,
            Func<ValueTask> next,
            CancellationToken ct
        )
        {
            CorrelationId = ctx.CorrelationId;
            ctx.Cancel = true;
            ctx.CancelReason = "blocked";
            await next().ConfigureAwait(false);
        }
    }

    private sealed class ThrowingProbeBehavior : IEventBehavior<ProbeEvent>
    {
        public ValueTask InvokeAsync(
            ProbeEvent env,
            EventContext ctx,
            Func<ValueTask> next,
            CancellationToken ct
        )
        {
            throw new InvalidOperationException("plugin failed");
        }
    }

    private sealed class CountingProbeHandler : IEventHandler<ProbeEvent>
    {
        public int Count { get; private set; }

        public ValueTask HandleAsync(ProbeEvent env, EventContext ctx, CancellationToken ct)
        {
            Count++;

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>A cancellable event of the test's own: the hotel ones it borrowed left with the cut.</summary>
    private sealed record ProbeEvent(int ActorPlayerId, string Name, int RoomId, int Cost) : IEvent;
}
