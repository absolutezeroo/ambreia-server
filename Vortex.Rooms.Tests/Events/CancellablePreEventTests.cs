using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Events;
using Vortex.Protocol.Messages.Outgoing.Room.Chat;
using Vortex.Rooms.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Events;

/// <summary>
/// Until now exactly one of the ~120 events on the bus was published cancellably, so an outside
/// behaviour could react to everything and refuse nothing. Chat is the cheapest of the four gates
/// to drive end to end, and all four are the same three lines: publish, read <c>Cancel</c>, unwind
/// through the refusal path that already existed.
/// </summary>
public sealed class CancellablePreEventTests
{
    private const string LINE = "hello room";

    [Fact]
    public async Task ChatIsAnnouncedBeforeAnyoneSeesIt()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        await SayAsync(harness).ConfigureAwait(true);

        PlayerChattingEvent chatting = harness
            .PublishedEvents.OfType<PlayerChattingEvent>()
            .Should()
            .ContainSingle()
            .Subject;

        chatting.PlayerId.Should().Be(RoomHarness.Owner);
        chatting.Message.Should().Be(LINE);
        chatting.TargetPlayerId.Should().BeNull();

        harness.BroadcastToRoom.OfType<ChatMessageComposer>().Should().ContainSingle();
    }

    [Fact]
    public async Task CancellingTheChatEventDropsTheLine()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        harness.CancelWhen = e => e is PlayerChattingEvent;

        await SayAsync(harness).ConfigureAwait(true);

        // Dropped for everyone, the speaker included: the room must not have echoed it before the
        // veto was read, or a refused line still shows on the sender's screen.
        harness.BroadcastToRoom.OfType<ChatMessageComposer>().Should().BeEmpty();
    }

    private static Task SayAsync(RoomHarness harness)
    {
        harness.PutRealPlayerInRoom(RoomHarness.Owner, 1, 1);

        return harness.Grain.ChatSystem.SendChatFromPlayerAsync(
            RoomHarness.Owner,
            LINE,
            default,
            links: [],
            trackingId: 0
        );
    }
}
