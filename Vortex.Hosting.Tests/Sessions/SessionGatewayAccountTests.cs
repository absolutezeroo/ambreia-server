using System.Collections.Generic;
using System.Linq;
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
using Vortex.Protocol.Messages.Outgoing.Handshake;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Hosting.Tests.Sessions;

/// <summary>
/// The account phase of a socket (spec F3a §5.2): mapped to the account, replaced by a second
/// connection of the same account, left for the player on entering the world, released on close.
/// </summary>
public sealed class SessionGatewayAccountTests
{
    private const int ACCOUNT_ID = 5;

    private readonly List<string> _accountCalls = [];
    private readonly List<string> _closed = [];
    private readonly List<(string Key, IComposer Composer)> _sent = [];
    private readonly SessionGateway _gateway;
    private long _generation;

    public SessionGatewayAccountTests() =>
        _gateway = new SessionGateway(
            Grains(),
            FakeProxy.Create<IEventPublisher>(_ => null),
            Options.Create(new NetworkingConfig()),
            NullLogger<SessionGateway>.Instance
        );

    [Fact]
    public async Task AnAccountSession_IsMappedAndRegistered()
    {
        SessionKey a = await ConnectAsync("a");

        await _gateway.AddSessionToAccountAsync(a, ACCOUNT_ID);

        _gateway.GetAccountId(a).Should().Be(ACCOUNT_ID);
        _accountCalls.Should().Equal("begin", "register:1");
    }

    [Fact]
    public async Task ASecondAccountSession_ReplacesTheFirst()
    {
        SessionKey a = await ConnectAsync("a");
        SessionKey b = await ConnectAsync("b");

        await _gateway.AddSessionToAccountAsync(a, ACCOUNT_ID);
        await _gateway.AddSessionToAccountAsync(b, ACCOUNT_ID);

        _gateway.GetAccountId(a).Should().Be(0);
        _gateway.GetAccountId(b).Should().Be(ACCOUNT_ID);
        _closed.Should().Equal("a");
        _sent
            .Should()
            .ContainSingle()
            .Which.Should()
            .Match<(string Key, IComposer Composer)>(s =>
                s.Key == "a"
                && s.Composer is DisconnectReasonEventMessageComposer
                && ((DisconnectReasonEventMessageComposer)s.Composer).Reason
                    == DisconnectReasonEventMessageComposer.ConcurrentLogin
            );
        _accountCalls.Should().Equal("begin", "register:1", "unregister", "begin", "register:2");
    }

    [Fact]
    public async Task EnteringTheWorld_LeavesTheAccountPhase()
    {
        SessionKey a = await ConnectAsync("a");
        await _gateway.AddSessionToAccountAsync(a, ACCOUNT_ID);

        await _gateway.AddSessionToPlayerAsync(a, 9);

        _gateway.GetAccountId(a).Should().Be(0);
        _gateway.GetPlayerId(a).Value.Should().Be(9);
        _accountCalls.Last().Should().Be("unregister");
        _closed.Should().BeEmpty("the socket carries on, as the player");
    }

    [Fact]
    public async Task ClosingTheAccountSocket_ReleasesTheAccount()
    {
        SessionKey a = await ConnectAsync("a");
        await _gateway.AddSessionToAccountAsync(a, ACCOUNT_ID);

        await _gateway.RemoveSessionAsync(a, CancellationToken.None);

        _gateway.GetAccountId(a).Should().Be(0);
        _accountCalls.Last().Should().Be("unregister");

        SessionKey b = await ConnectAsync("b");
        await _gateway.AddSessionToAccountAsync(b, ACCOUNT_ID);

        _gateway.GetAccountId(b).Should().Be(ACCOUNT_ID);
        _closed.Should().BeEmpty();
        _accountCalls.Last().Should().Be("register:2");
    }

    [Fact]
    public async Task AGamePacketInTheAccountPhase_HasNoPlayer()
    {
        SessionKey a = await ConnectAsync("a");

        await _gateway.AddSessionToAccountAsync(a, ACCOUNT_ID);

        _gateway
            .GetPlayerId(a)
            .Value.Should()
            .Be(-1, "game handlers refuse a session without a player");
    }

    private async Task<SessionKey> ConnectAsync(string name)
    {
        SessionKey key = new(name);
        ISessionContext session = FakeProxy.Create<ISessionContext>(call =>
        {
            switch (call.Method.Name)
            {
                case nameof(ISessionContext.CloseSessionAsync):
                    _closed.Add(name);
                    break;
                case nameof(ISessionContext.SendComposerAsync):
                    _sent.Add((name, (IComposer)call.Args![0]!));
                    break;
            }

            return null;
        });

        (await _gateway.AddSessionAsync(key, session)).Should().BeTrue();

        return key;
    }

    private IGrainFactory Grains() =>
        FakeProxy.Create<IGrainFactory>(call =>
            call.Method.Name switch
            {
                nameof(IGrainFactory.CreateObjectReference) =>
                    FakeProxy.Create<ISessionContextObserver>(_ => null),
                nameof(IGrainFactory.GetGrain)
                    when call.Method.GetGenericArguments()[0] == typeof(IAccountSessionGrain) =>
                    AccountSession(),
                nameof(IGrainFactory.GetGrain) => FakeProxy.Create<IPlayerPresenceGrain>(p =>
                    p.Method.Name == nameof(IPlayerPresenceGrain.BeginSessionAsync)
                        ? Task.FromResult(1L)
                        : null
                ),
                _ => null,
            }
        );

    private IAccountSessionGrain AccountSession() =>
        FakeProxy.Create<IAccountSessionGrain>(call =>
        {
            switch (call.Method.Name)
            {
                case nameof(IAccountSessionGrain.BeginSessionAsync):
                    _accountCalls.Add("begin");

                    return Task.FromResult(++_generation);
                case nameof(IAccountSessionGrain.RegisterSessionObserverAsync):
                    _accountCalls.Add($"register:{call.Args![1]}");
                    break;
                case nameof(IAccountSessionGrain.UnregisterSessionObserverAsync):
                    _accountCalls.Add("unregister");
                    break;
            }

            return null;
        });
}
