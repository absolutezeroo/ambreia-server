using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Events;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Primitives.Rooms.Providers;
using Vortex.Rooms.Events;
using Vortex.Rooms.Providers;
using Vortex.Rooms.Tests.Support;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Events;

/// <summary>
/// The in-room event stream — chat, clicks, an avatar stepping onto a furni, a wired stack firing —
/// used to reach only the three systems <c>RoomGrain</c> attaches by hand, because nothing scanned
/// for <see cref="IRoomEventListener" />. A plugin could not observe a room at all.
/// </summary>
public sealed class RoomEventListenerExtensionTests
{
    [Fact]
    public async Task AMarkedListener_IsRegistered_AndBuiltPerRoom()
    {
        RoomEventListenerProvider provider = NewProvider();

        await ProcessAsync(provider).ConfigureAwait(true);

        IRoomGrain roomOne = FakeProxy.Create<IRoomGrain>(_ => null);
        IRoomGrain roomTwo = FakeProxy.Create<IRoomGrain>(_ => null);

        List<IRoomEventListener> forRoomOne = [.. provider.BuildListenersForRoom(roomOne)];
        List<IRoomEventListener> forRoomTwo = [.. provider.BuildListenersForRoom(roomTwo)];

        forRoomOne.Should().ContainSingle().Which.Should().BeOfType<MarkedListener>();

        // Per room, not shared: a listener holds the room it was built for, so one instance handed
        // to every room would attribute one room's events to another.
        forRoomOne[0].Should().NotBeSameAs(forRoomTwo[0]);
        ((MarkedListener)forRoomOne[0]).Room.Should().BeSameAs(roomOne);
    }

    [Fact]
    public async Task AnUnmarkedListener_IsIgnored()
    {
        // RoomRollerSystem and GameScoreboardPresenter both implement the interface
        // and are attached by the grain in a fixed order it depends on. Scanning the interface alone
        // would build a second copy of each into every room.
        RoomEventListenerProvider provider = NewProvider();

        await ProcessAsync(provider).ConfigureAwait(true);

        provider
            .BuildListenersForRoom(FakeProxy.Create<IRoomGrain>(_ => null))
            .Should()
            .NotContain(listener => listener is UnmarkedListener);
    }

    [Fact]
    public async Task DeregisteringTheBatch_TakesTheListenerBackOut()
    {
        // The disposable a processor returns is the deregistration, which is what makes a plugin
        // unload clean rather than leaving dead factories behind.
        RoomEventListenerProvider provider = NewProvider();

        using (await ProcessAsync(provider).ConfigureAwait(true)) { }

        provider.BuildListenersForRoom(FakeProxy.Create<IRoomGrain>(_ => null)).Should().BeEmpty();
    }

    [Fact]
    public async Task AThrowingListener_DoesNotStopTheOthers()
    {
        // The listener list is no longer only the room's own systems. A contributed listener that
        // throws used to abandon every listener after it and surface the failure inside whatever
        // gameplay path raised the event.
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);
        RecordingListener after = new();

        harness.Grain.EventModule.Register(new ThrowingListener());
        harness.Grain.EventModule.Register(after);

        await harness
            .Grain.PublishRoomEventAsync(
                new PeriodicRoomEvent { RoomId = harness.Grain.RoomId },
                CancellationToken.None
            )
            .ConfigureAwait(true);

        after.Seen.Should().Be(1);
    }

    private static RoomEventListenerProvider NewProvider() =>
        new(new ServiceCollection().BuildServiceProvider());

    private static Task<IDisposable> ProcessAsync(IRoomEventListenerProvider provider) =>
        new RoomEventListenerFeatureProcessor(
            provider,
            NullLogger<RoomEventListenerFeatureProcessor>.Instance
        ).ProcessAsync(
            typeof(RoomEventListenerExtensionTests).Assembly,
            new ServiceCollection().BuildServiceProvider()
        );

    private sealed class ThrowingListener : IRoomEventListener
    {
        public Task OnRoomEventAsync(RoomEvent evt, CancellationToken ct) =>
            throw new InvalidOperationException("listener is broken");
    }

    private sealed class RecordingListener : IRoomEventListener
    {
        public int Seen { get; private set; }

        public Task OnRoomEventAsync(RoomEvent evt, CancellationToken ct)
        {
            Seen++;

            return Task.CompletedTask;
        }
    }
}

/// <summary>
/// Top-level rather than nested inside the test class on purpose: the scan takes <c>IsPublic</c>
/// types, and a nested type is never <c>IsPublic</c> however it is declared. Nesting these would
/// have them skipped for the wrong reason and the test would prove nothing.
/// </summary>
[RoomEventListener]
public sealed class MarkedListener(IRoomGrain room) : IRoomEventListener
{
    public IRoomGrain Room { get; } = room;

    public Task OnRoomEventAsync(RoomEvent evt, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>Implements the interface without the attribute, like the room's own systems do.</summary>
public sealed class UnmarkedListener : IRoomEventListener
{
    public Task OnRoomEventAsync(RoomEvent evt, CancellationToken ct) => Task.CompletedTask;
}
