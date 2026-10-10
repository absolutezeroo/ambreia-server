using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans;
using Vortex.Networking.Configuration;
using Vortex.Networking.Session;
using Vortex.Primitives.Events;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Orleans.Observers;
using Vortex.Primitives.Players.Grains;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Hosting.Tests.Networking;

/// <summary>
/// A socket that never presents a ticket is a scanner or a slot held on purpose: with nothing
/// closing it, a few thousand of them fill the session caps for good. The gateway gives a connection
/// <c>HandshakeTimeout</c> to become an account, and closes it otherwise.
/// </summary>
public sealed class HandshakeDeadlineTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task ASocketWithoutATicket_IsClosedWhenTheDeadlinePasses()
    {
        ManualTimeProvider clock = Clock();
        SessionGateway gateway = Gateway(clock, Timeout);
        Probe probe = new();
        await gateway.AddSessionAsync(new SessionKey("mute"), probe.Session);

        clock.Advance(Timeout - TimeSpan.FromSeconds(1));
        probe.Closed.Should().BeFalse("the client still has a second");

        clock.Advance(TimeSpan.FromSeconds(2));
        probe.Closed.Should().BeTrue();
    }

    [Fact]
    public async Task ASocketThatBecameAnAccount_IsLeftAlone()
    {
        ManualTimeProvider clock = Clock();
        SessionGateway gateway = Gateway(clock, Timeout);
        Probe probe = new();
        SessionKey key = new("player");
        await gateway.AddSessionAsync(key, probe.Session);
        await gateway.AddSessionToAccountAsync(key, accountId: 7);

        clock.Advance(Timeout * 3);

        probe.Closed.Should().BeFalse();
    }

    [Fact]
    public async Task ASocketClosedByItsPeer_ArmsNothingLeftBehind()
    {
        ManualTimeProvider clock = Clock();
        SessionGateway gateway = Gateway(clock, Timeout);
        Probe probe = new();
        SessionKey key = new("gone");
        await gateway.AddSessionAsync(key, probe.Session);
        await gateway.RemoveSessionAsync(key, CancellationToken.None);

        clock.Advance(Timeout * 3);

        probe.Closed.Should().BeFalse("the transport already closed it; nothing to do twice");
    }

    [Fact]
    public async Task AZeroTimeout_DisablesTheDeadline()
    {
        ManualTimeProvider clock = Clock();
        SessionGateway gateway = Gateway(clock, TimeSpan.Zero);
        Probe probe = new();
        await gateway.AddSessionAsync(new SessionKey("mute"), probe.Session);

        clock.Advance(TimeSpan.FromHours(1));

        probe.Closed.Should().BeFalse();
    }

    private static ManualTimeProvider Clock() =>
        new(new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero));

    private static SessionGateway Gateway(TimeProvider clock, TimeSpan handshakeTimeout) =>
        new(
            FakeProxy.Create<IGrainFactory>(call =>
                call.Method.Name switch
                {
                    nameof(IGrainFactory.CreateObjectReference) =>
                        FakeProxy.Create<ISessionContextObserver>(_ => null),
                    nameof(IGrainFactory.GetGrain) => FakeProxy.Create<IAccountSessionGrain>(_ =>
                        null
                    ),
                    _ => null,
                }
            ),
            FakeProxy.Create<IEventPublisher>(_ => null),
            Options.Create(new NetworkingConfig { HandshakeTimeout = handshakeTimeout }),
            NullLogger<SessionGateway>.Instance,
            clock
        );

    /// <summary>A session that remembers whether the gateway closed it.</summary>
    private sealed class Probe
    {
        public bool Closed { get; private set; }

        public ISessionContext Session { get; }

        public Probe() =>
            Session = FakeProxy.Create<ISessionContext>(call =>
            {
                if (call.Method.Name == nameof(ISessionContext.CloseSessionAsync))
                {
                    Closed = true;

                    return Task.CompletedTask;
                }

                return null;
            });
    }
}
