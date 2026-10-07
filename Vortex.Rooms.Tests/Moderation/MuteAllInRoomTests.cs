using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.Players;
using Vortex.Protocol.Messages.Outgoing.Room.Chat;
using Vortex.Protocol.Messages.Outgoing.Roomsettings;
using Vortex.Rooms.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Moderation;

/// <summary>
/// The room-info panel's "mute all" switch, which this server parsed and then did nothing with.
/// </summary>
/// <remarks>
/// It is a toggle, not a sanction: no target, no duration, and it is gone when the room unloads. It
/// silences the room for everyone except the people who can build in it, which is the rule the
/// reference emulator enforces too (RoomChatManager:295) and the only thing that makes the switch
/// usable — an owner who mutes the room has to stay able to say why.
/// </remarks>
public sealed class MuteAllInRoomTests
{
    private const string LINE = "hello";

    [Fact]
    public async Task TheOwnerFlipsTheSwitchAndTheRoomIsToldWhereItLanded()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        bool applied = await harness
            .Grain.ToggleAllInRoomMuteAsync(harness.ContextFor(RoomHarness.Owner))
            .ConfigureAwait(true);

        applied.Should().BeTrue();
        harness.Grain._state.AllInRoomMuted.Should().BeTrue();

        harness
            .BroadcastToRoom.OfType<MuteAllInRoomEventMessageComposer>()
            .Should()
            .ContainSingle()
            .Which.AllMuted.Should()
            .BeTrue();

        // A toggle both ways: pressing it again has to give the room its voice back.
        await harness
            .Grain.ToggleAllInRoomMuteAsync(harness.ContextFor(RoomHarness.Owner))
            .ConfigureAwait(true);

        harness.Grain._state.AllInRoomMuted.Should().BeFalse();
        harness.BroadcastToRoom.OfType<MuteAllInRoomEventMessageComposer>().Should().HaveCount(2);
    }

    [Fact]
    public async Task AVisitorCannotSilenceSomebodyElsesRoom()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        bool applied = await harness
            .Grain.ToggleAllInRoomMuteAsync(harness.ContextFor(RoomHarness.Stranger))
            .ConfigureAwait(true);

        applied.Should().BeFalse();
        harness.Grain._state.AllInRoomMuted.Should().BeFalse();
        harness.BroadcastToRoom.OfType<MuteAllInRoomEventMessageComposer>().Should().BeEmpty();
    }

    [Fact]
    public async Task WhileMutedAVisitorsLineNeverReachesTheRoom()
    {
        // canManipulate:false — the harness hands Stranger build rights by default, and rights are
        // exactly what the switch spares.
        RoomHarness harness = await RoomHarness
            .CreateAsync(canManipulate: false)
            .ConfigureAwait(true);
        harness.Grain._state.AllInRoomMuted = true;
        harness.PutRealPlayerInRoom(RoomHarness.Stranger, 1, 1);

        await SayAsync(harness, RoomHarness.Stranger).ConfigureAwait(true);

        harness.BroadcastToRoom.OfType<ChatMessageComposer>().Should().BeEmpty();
    }

    [Fact]
    public async Task WhileMutedTheOwnerKeepsTalking()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);
        harness.Grain._state.AllInRoomMuted = true;
        harness.PutRealPlayerInRoom(RoomHarness.Owner, 1, 1);

        await SayAsync(harness, RoomHarness.Owner).ConfigureAwait(true);

        harness.BroadcastToRoom.OfType<ChatMessageComposer>().Should().ContainSingle();
    }

    [Fact]
    public async Task TheRoomCardOffersTheButtonToRightsHoldersOnly()
    {
        RoomHarness harness = await RoomHarness
            .CreateAsync(canManipulate: false)
            .ConfigureAwait(true);
        harness.Grain._state.AllInRoomMuted = true;

        RoomMuteStateSnapshot owner = await harness
            .Grain.GetMuteStateAsync(RoomHarness.Owner)
            .ConfigureAwait(true);
        RoomMuteStateSnapshot stranger = await harness
            .Grain.GetMuteStateAsync(RoomHarness.Stranger)
            .ConfigureAwait(true);

        owner.CanMute.Should().BeTrue();
        stranger.CanMute.Should().BeFalse();

        // The room's own state, the same for both of them.
        owner.AllInRoomMuted.Should().BeTrue();
        stranger.AllInRoomMuted.Should().BeTrue();
    }

    private static Task SayAsync(RoomHarness harness, PlayerId speaker) =>
        harness.Grain.ChatSystem.SendChatFromPlayerAsync(
            speaker,
            LINE,
            default,
            links: [],
            trackingId: 0
        );
}
