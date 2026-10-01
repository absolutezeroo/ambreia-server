using FluentAssertions;
using Orleans.Concurrency;
using Vortex.Primitives.FriendList.Grains;
using Xunit;

namespace Vortex.Players.Tests.Messenger;

/// <summary>
/// <c>SendMessageAsync</c> awaits the receiver's <c>ReceiveMessageAsync</c> with its own turn held
/// open. Two friends writing to each other at the same moment each waited on the other's turn and
/// both stood still until the 30 s call timeout; the receiving half has to interleave.
/// </summary>
public sealed class MessengerReceiveInterleaveTests
{
    [Fact]
    public void ReceiveMessage_InterleavesWithTheReceiversOwnSend()
    {
        typeof(IMessengerGrain)
            .GetMethod(nameof(IMessengerGrain.ReceiveMessageAsync))!
            .IsDefined(typeof(AlwaysInterleaveAttribute), inherit: false)
            .Should()
            .BeTrue();
    }
}
