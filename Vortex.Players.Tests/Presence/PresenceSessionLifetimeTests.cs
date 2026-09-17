using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans;
using Vortex.Players.Configuration;
using Vortex.Players.Grains;
using Vortex.Primitives.Events;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Orleans.Observers;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Players.Tests.Presence;

/// <summary>
/// The presence grain lives as long as the socket it serves.
/// </summary>
/// <remarks>
/// <para>
/// It was an ordinary grain, so Orleans collected it after <c>GrainCollectionAge</c> -- two minutes
/// -- without a message, and nothing sent it one but the player's own packets. Collection runs
/// <c>OnDeactivateAsync</c>, which unregisters the session observer and walks the player out of
/// their room. A connected player who went quiet for two minutes -- a backgrounded tab, a sleeping
/// laptop, a cable pulled without a FIN -- came back a ghost: still connected, gone from the room,
/// offline to their friends, and mute, because the fresh activation has no observer and every reply
/// queues behind one that is never drained. Only a new SSO ticket put it right.
/// </para>
/// <para>
/// So the activation is pinned while it holds a socket and released when it does not. Both halves
/// matter: without the first the player is a ghost, without the second every player who ever logged
/// in is pinned for the lifetime of the silo.
/// </para>
/// </remarks>
public sealed class PresenceSessionLifetimeTests
{
    [Fact]
    public async Task RegisteringASession_PinsTheActivation()
    {
        Recorder recorder = new();

        await Build(recorder)
            .RegisterSessionObserverAsync(FakeProxy.Create<ISessionContextObserver>(_ => null), 1)
            .ConfigureAwait(true);

        recorder
            .Delays.Should()
            .ContainSingle(
                "nothing but the player's own traffic was keeping this activation out of the "
                    + "collector, and a quiet client sends none"
            )
            .Which.Should()
            .Be(TimeSpan.MaxValue);

        recorder.Deactivations.Should().Be(0);
    }

    /// <summary>
    ///     Two sockets for one account, the second replacing the first, and the first still on its
    ///     way in when it does. Attaching it would put a socket nobody is listening to in front of
    ///     the live one: the player is connected as far as the server is concerned and hears
    ///     nothing, which is the ghost this whole suite is named for arriving by another door.
    /// </summary>
    [Fact]
    public async Task ASocketReplacedWhileItWasStillAttaching_DoesNotTakeThePlayerBack()
    {
        Recorder recorder = new();
        PlayerPresenceGrain grain = Build(recorder);

        // Both sockets claim their place in the order the gateway decided it, which is what
        // BeginSessionAsync exists to record.
        long first = await grain.BeginSessionAsync(CancellationToken.None).ConfigureAwait(true);
        long second = await grain.BeginSessionAsync(CancellationToken.None).ConfigureAwait(true);

        second.Should().BeGreaterThan(first, "the replacement was decided second");

        ISessionContextObserver live = FakeProxy.Create<ISessionContextObserver>(_ => null);

        await grain.RegisterSessionObserverAsync(live, second).ConfigureAwait(true);

        // And now the one it replaced finally lands.
        await grain
            .RegisterSessionObserverAsync(
                FakeProxy.Create<ISessionContextObserver>(_ => null),
                first
            )
            .ConfigureAwait(true);

        (await grain.IsOnlineAsync(CancellationToken.None).ConfigureAwait(true))
            .Should()
            .BeTrue("the live socket is still the one holding the player");

        grain.AttachedObserver.Should().BeSameAs(live, "the replaced socket did not take it back");
    }

    [Fact]
    public async Task DroppingTheSession_ReleasesTheActivation()
    {
        Recorder recorder = new();
        PlayerPresenceGrain grain = Build(recorder);

        await grain
            .RegisterSessionObserverAsync(FakeProxy.Create<ISessionContextObserver>(_ => null), 1)
            .ConfigureAwait(true);
        await grain.UnregisterSessionObserverAsync(CancellationToken.None).ConfigureAwait(true);

        recorder
            .Deactivations.Should()
            .Be(
                1,
                "an activation with no socket is ordinary state again; pinning every one of them "
                    + "for the lifetime of the silo is the other way to get this wrong"
            );
    }

    private static PlayerPresenceGrain Build(Recorder recorder) =>
        GrainActivationContext.CreateWithIntegerKey<PlayerPresenceGrain>(
            primaryKey: 42,
            recorder.Observe,
            [
                FakeProxy.Create<IGrainFactory>(_ => null),
                FakeProxy.Create<IEventPublisher>(_ => Task.CompletedTask),
                NullLogger<PlayerPresenceGrain>.Instance,
                FakeProxy.Create<IVortexMetrics>(_ => null),
                Options.Create(new PlayerPresenceConfig()),
            ]
        );

    /// <summary>
    /// What the grain asked the runtime to do with its own lifetime. <c>DelayDeactivation</c> and
    /// <c>DeactivateOnIdle</c> reach <c>IGrainRuntime</c>, not the activation context, so this
    /// listens to everything the grain resolves and picks the two calls out by name.
    /// </summary>
    private sealed class Recorder
    {
        public List<TimeSpan> Delays { get; } = [];
        public int Deactivations { get; private set; }

        public object? Observe(ProxyCall call)
        {
            switch (call.Method.Name)
            {
                case "DelayDeactivation":
                    Delays.Add(call.Args?.OfType<TimeSpan>().FirstOrDefault() ?? TimeSpan.Zero);
                    break;
                case "DeactivateOnIdle":
                    Deactivations++;
                    break;
            }

            // Recorded, never answered: the stub behind this decides what the call returns.
            return null;
        }
    }
}
