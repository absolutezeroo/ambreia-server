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
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Grains;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Hosting.Tests.Networking;

/// <summary>
/// A socket that closes while its login waits for the mapping gate. The removal used to read the
/// mapping outside the gate, find nothing yet, and finish; the login then mapped the closed socket
/// to the player, who stayed "online" with a dead observer pinned in their presence grain.
/// </summary>
public sealed class SessionGatewayRaceTests
{
    private static readonly PlayerId Busy = new(1);
    private static readonly PlayerId Closing = new(2);

    [Fact]
    public async Task ASocketClosedWhileItsLoginWaits_IsNeverMappedToThePlayer()
    {
        TaskCompletionSource<long> busyBegin = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        SessionGateway gateway = new(
            Grains(busyBegin.Task),
            FakeProxy.Create<IEventPublisher>(_ => null),
            Options.Create(new NetworkingConfig()),
            NullLogger<SessionGateway>.Instance
        );

        SessionKey busyKey = new("busy");
        SessionKey closingKey = new("closing");
        await gateway.AddSessionAsync(busyKey, FakeProxy.Create<ISessionContext>(_ => null));
        await gateway.AddSessionAsync(closingKey, FakeProxy.Create<ISessionContext>(_ => null));

        // Another login holds the gate...
        Task busyLogin = gateway.AddSessionToPlayerAsync(busyKey, Busy);

        // ...so this one queues behind it, and its socket closes meanwhile.
        Task closingLogin = gateway.AddSessionToPlayerAsync(closingKey, Closing);
        Task removal = gateway.RemoveSessionAsync(closingKey, CancellationToken.None);

        busyBegin.SetResult(1);
        await Task.WhenAll(busyLogin, closingLogin, removal);

        gateway.GetPlayerId(closingKey).Value.Should().BeLessThanOrEqualTo(0);
        gateway.IsOnline(Closing).Should().BeFalse();
        gateway.IsOnline(Busy).Should().BeTrue();
    }

    private static IGrainFactory Grains(Task<long> busyBegin) =>
        FakeProxy.Create<IGrainFactory>(call =>
            call.Method.Name switch
            {
                nameof(IGrainFactory.CreateObjectReference) =>
                    FakeProxy.Create<ISessionContextObserver>(_ => null),
                nameof(IGrainFactory.GetGrain) => Presence(
                    (long)call.Args![0]! == Busy.Value ? busyBegin : Task.FromResult(1L)
                ),
                _ => null,
            }
        );

    private static IPlayerPresenceGrain Presence(Task<long> begin) =>
        FakeProxy.Create<IPlayerPresenceGrain>(call =>
            call.Method.Name == nameof(IPlayerPresenceGrain.BeginSessionAsync) ? begin : null
        );
}
