using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Events;
using Vortex.Rooms.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Events;

/// <summary>
/// Placing, moving and picking furniture up publish without holding the room's turn open
/// (INFRA-EVENT-058). The handlers behind those three events call the acting player's quest,
/// achievement and daily-task grains, so awaiting the publication made every click of a build
/// session wait for another grain — with everyone standing in the room waiting behind it.
/// </summary>
public sealed class DetachedPublicationTests
{
    [Fact]
    public async Task AHandlerThatNeverAnswers_DoesNotHoldTheRoom()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        // A publication that never completes: the slowest handler imaginable.
        TaskCompletionSource pending = new();
        harness.PublishResult = pending.Task;

        Action publish = () => harness.Grain.PublishDetached(new ProbeEvent(1));

        publish.Should().NotThrow();
        harness.PublishedEvents.OfType<ProbeEvent>().Should().ContainSingle();

        pending.SetResult();
    }

    [Fact]
    public async Task AHandlerThatFails_DoesNotFailTheActionThatCausedIt()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        harness.PublishResult = Task.FromException(new InvalidOperationException("handler broke"));

        Action publish = () => harness.Grain.PublishDetached(new ProbeEvent(1));

        // Observed and logged rather than thrown: a quest handler that is down must not make the
        // sofa fail to move.
        publish.Should().NotThrow();
    }

    /// <summary>Any event will do: what is under test is the detached publication, not the event.</summary>
    private sealed record ProbeEvent(int RoomId) : IEvent;
}
