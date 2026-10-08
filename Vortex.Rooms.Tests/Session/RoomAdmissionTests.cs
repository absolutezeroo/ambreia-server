using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Events;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Protocol.Messages.Outgoing.Room.Engine;
using Vortex.Protocol.Messages.Outgoing.Room.Session;
using Vortex.Rooms.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Session;

/// <summary>
/// The room's door (<c>RoomGrain.AdmitAsync</c>): a full room, a locked door and a wrong password
/// refuse; rights and a resume within the grace period skip all three; the cancellable entry event
/// is the last gate; past it, the burst.
/// </summary>
public sealed class RoomAdmissionTests
{
    private static readonly PlayerId Visitor = new(303);

    [Fact]
    public async Task AFullRoom_Refuses()
    {
        RoomHarness harness = await RoomHarness.CreateAsync();
        harness.Grain._state.RoomSnapshot = harness.Grain._state.RoomSnapshot with
        {
            PlayersMax = 1,
        };
        harness.PutRealPlayerInRoom(RoomHarness.Stranger, 1, 1);

        (await AdmitAsync(harness, Visitor)).Refusal.Should().Be(RoomConnectionErrorType.RoomFull);
    }

    [Theory]
    [InlineData(RoomDoorModeType.Locked, "")]
    [InlineData(RoomDoorModeType.Password, "wrong")]
    public async Task AClosedDoor_Refuses(RoomDoorModeType door, string password)
    {
        RoomHarness harness = await RoomHarness.CreateAsync();
        harness.Grain._state.RoomSnapshot = harness.Grain._state.RoomSnapshot with
        {
            DoorMode = door,
            Password = "secret",
        };

        (await AdmitAsync(harness, Visitor, password))
            .Refusal.Should()
            .Be(RoomConnectionErrorType.NoEntry);
    }

    [Fact]
    public async Task TheRightPassword_Opens()
    {
        RoomHarness harness = await RoomHarness.CreateAsync();
        harness.Grain._state.RoomSnapshot = harness.Grain._state.RoomSnapshot with
        {
            DoorMode = RoomDoorModeType.Password,
            Password = "secret",
        };

        (await AdmitAsync(harness, Visitor, "secret")).Refusal.Should().BeNull();
    }

    /// <summary>The owner has rights, and a resumed player was admitted before the cut.</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task RightsOrAResume_SkipTheDoor(bool owner, bool resumed)
    {
        RoomHarness harness = await RoomHarness.CreateAsync();
        harness.Grain._state.RoomSnapshot = harness.Grain._state.RoomSnapshot with
        {
            DoorMode = RoomDoorModeType.Locked,
            PlayersMax = 1,
        };
        harness.PutRealPlayerInRoom(RoomHarness.Stranger, 1, 1);

        (await AdmitAsync(harness, owner ? RoomHarness.Owner : Visitor, resumed: resumed))
            .Refusal.Should()
            .BeNull();
    }

    [Fact]
    public async Task ACancelledEntryEvent_Refuses_WithNoBurst()
    {
        RoomHarness harness = await RoomHarness.CreateAsync();
        harness.CancelWhen = e => e is PlayerEnteringRoomEvent;

        RoomAdmissionSnapshot admission = await AdmitAsync(harness, Visitor);

        admission.Refusal.Should().Be(RoomConnectionErrorType.NoEntry);
        admission.Burst.Should().BeEmpty();
    }

    [Fact]
    public async Task AnAdmission_CarriesTheBurst()
    {
        RoomHarness harness = await RoomHarness.CreateAsync();

        RoomAdmissionSnapshot admission = await AdmitAsync(harness, Visitor);

        admission.Refusal.Should().BeNull();
        admission.Burst.First().Should().BeOfType<RoomReadyMessageComposer>();
        admission.Burst.OfType<RoomEntryInfoMessageComposer>().Should().ContainSingle();
    }

    private static Task<RoomAdmissionSnapshot> AdmitAsync(
        RoomHarness harness,
        PlayerId player,
        string password = "",
        bool resumed = false
    ) =>
        harness.Grain.AdmitAsync(
            harness.ContextFor(player),
            password,
            resumed,
            CancellationToken.None
        );
}
