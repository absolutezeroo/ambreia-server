using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vortex.Players.Configuration;
using Vortex.Players.Grains;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Orleans.Observers;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Players.Tests.Accounts;

/// <summary>
/// The account phase's socket (spec F3a §5.2): the character screen is answered through it, in
/// order, off the grain's turn, and a socket replaced while it was still attaching does not take
/// the account back.
/// </summary>
public sealed class AccountSessionGrainTests
{
    private static AccountSessionGrain Build(int maxQueue = 500) =>
        GrainActivationContext.CreateWithIntegerKey<AccountSessionGrain>(
            7,
            Options.Create(new PlayerPresenceConfig { MaxOutgoingQueueSize = maxQueue }),
            NullLogger<AccountSessionGrain>.Instance
        );

    private static ISessionContextObserver Recording(
        List<IComposer> received,
        Task? firstSend = null
    ) =>
        FakeProxy.Create<ISessionContextObserver>(call =>
        {
            received.Add((IComposer)call.Args![0]!);

            return received.Count == 1 ? firstSend : null;
        });

    private static IComposer Composer() => FakeProxy.Create<IComposer>(_ => null);

    [Fact]
    public async Task RegisteringASession_SendsInOrder()
    {
        AccountSessionGrain grain = Build();
        List<IComposer> received = [];
        IComposer first = Composer();
        IComposer second = Composer();

        await grain.RegisterSessionObserverAsync(Recording(received), 1);
        await grain.SendComposerAsync(first, second);

        received.Should().Equal(first, second);
    }

    [Fact]
    public async Task AnOlderGeneration_IsIgnored()
    {
        AccountSessionGrain grain = Build();
        List<IComposer> live = [];
        List<IComposer> replaced = [];

        await grain.RegisterSessionObserverAsync(Recording(live), 2);
        await grain.RegisterSessionObserverAsync(Recording(replaced), 1);
        await grain.SendComposerAsync(Composer());

        live.Should().ContainSingle("the newer socket still holds the account");
        replaced.Should().BeEmpty();
    }

    [Fact]
    public async Task WithoutASession_NothingIsSent()
    {
        AccountSessionGrain grain = Build();
        List<IComposer> received = [];

        await grain.SendComposerAsync(Composer());

        await grain.RegisterSessionObserverAsync(Recording(received), 1);
        await grain.UnregisterSessionObserverAsync();
        await grain.SendComposerAsync(Composer());

        received.Should().BeEmpty();
    }

    /// <summary>
    /// A client that stops reading fills its TCP window and the write blocks. Awaited inside the
    /// grain's turn, it held the unregister the gateway awaits under its global gate, and every
    /// login on the server waited behind one stalled socket.
    /// </summary>
    [Fact]
    public async Task ASocketThatStopsReading_DoesNotHoldTheGrainsTurn()
    {
        AccountSessionGrain grain = Build();
        TaskCompletionSource stalled = new();

        await grain.RegisterSessionObserverAsync(Recording([], stalled.Task), 1);

        grain.SendComposerAsync(Composer()).IsCompleted.Should().BeTrue();
        grain.UnregisterSessionObserverAsync().IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task AStalledSocket_StopsQueueingAtTheCap()
    {
        AccountSessionGrain grain = Build(maxQueue: 2);
        TaskCompletionSource stalled = new();
        List<IComposer> received = [];
        IComposer[] sent = [Composer(), Composer(), Composer(), Composer()];

        await grain.RegisterSessionObserverAsync(Recording(received, stalled.Task), 1);
        await grain.SendComposerAsync(sent[0]);
        // The first write is stuck; two fit behind it, the fourth is dropped.
        await grain.SendComposerAsync(sent[1], sent[2], sent[3]);
        stalled.SetResult();
        await Task.Yield();

        received.Should().Equal(sent[0], sent[1], sent[2]);
    }
}
