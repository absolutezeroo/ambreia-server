using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Vortex.Players.Grains;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Orleans.Observers;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Players.Tests.Accounts;

/// <summary>
/// The account phase's socket (spec F3a §5.2): the character screen is answered through it, in
/// order, and a socket replaced while it was still attaching does not take the account back.
/// </summary>
public sealed class AccountSessionGrainTests
{
    private static AccountSessionGrain Build() =>
        GrainActivationContext.CreateWithIntegerKey<AccountSessionGrain>(
            7,
            NullLogger<AccountSessionGrain>.Instance
        );

    private static ISessionContextObserver Recording(List<IComposer> received) =>
        FakeProxy.Create<ISessionContextObserver>(call =>
        {
            received.Add((IComposer)call.Args![0]!);

            return null;
        });

    [Fact]
    public async Task RegisteringASession_SendsInOrder()
    {
        AccountSessionGrain grain = Build();
        List<IComposer> received = [];
        IComposer first = FakeProxy.Create<IComposer>(_ => null);
        IComposer second = FakeProxy.Create<IComposer>(_ => null);

        long generation = await grain.BeginSessionAsync();
        await grain.RegisterSessionObserverAsync(Recording(received), generation);
        await grain.SendComposerAsync(first, second);

        received.Should().Equal(first, second);
    }

    [Fact]
    public async Task AnOlderGeneration_IsIgnored()
    {
        AccountSessionGrain grain = Build();
        List<IComposer> live = [];
        List<IComposer> replaced = [];

        long older = await grain.BeginSessionAsync();
        long newer = await grain.BeginSessionAsync();
        await grain.RegisterSessionObserverAsync(Recording(live), newer);
        await grain.RegisterSessionObserverAsync(Recording(replaced), older);
        await grain.SendComposerAsync(FakeProxy.Create<IComposer>(_ => null));

        live.Should().ContainSingle("the newer socket still holds the account");
        replaced.Should().BeEmpty();
    }

    [Fact]
    public async Task WithoutASession_NothingIsSent()
    {
        AccountSessionGrain grain = Build();
        List<IComposer> received = [];

        await grain.SendComposerAsync(FakeProxy.Create<IComposer>(_ => null));

        long generation = await grain.BeginSessionAsync();
        await grain.RegisterSessionObserverAsync(Recording(received), generation);
        await grain.UnregisterSessionObserverAsync();
        await grain.SendComposerAsync(FakeProxy.Create<IComposer>(_ => null));

        received.Should().BeEmpty();
    }
}
