using System;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Navigator.Enums;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.Orleans.Snapshots.Room.Settings;
using Vortex.Primitives.Packets;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Protocol.Messages.Outgoing.Navigator;
using Vortex.Revisions.Configuration;
using Xunit;
using Rev = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions.Tests.Navigator;

/// <summary>
/// The tail of GetGuestRoomResult, which changed shape in this revision and which we were still
/// writing the old way. The client (unknowns/_SafePkg_2064/_SafeCls_2063.as:48-51) reads
/// <c>canMute</c>, then <c>fromFloodSensitivity(readInteger())</c> — a single int — then
/// <c>openingConnection</c>. We used to write the five chat ints an older revision took here
/// (mode, bubble width, scroll speed, hear range, sensitivity); the four surplus ones made the
/// client read a chat setting as its opening-connection flag and left 16 bytes of the packet
/// unaccounted for. Chat mode, bubble width and scroll speed are per-account settings now and
/// travel in the account-preferences packet instead.
/// </summary>
public sealed class GetGuestRoomResultTailWireTests
{
    private static readonly Rev Revision = new(Options.Create(new ProtocolLimitsConfig()));

    [Theory]
    [InlineData(ChatFloodSensitivityType.Extra, true)]
    [InlineData(ChatFloodSensitivityType.Minimal, false)]
    public void EndsWithTheFloodSensitivityIntAndTheOpeningConnectionFlag(
        ChatFloodSensitivityType sensitivity,
        bool openingConnection
    )
    {
        ClientPacket body = Serialize(Composer(sensitivity, openingConnection));

        // Everything before the tail is the room block, the mod settings and canMute, each covered
        // by its own test; what this one pins is that exactly five bytes follow them.
        body.PopBytes(body.Remaining - 5);

        body.PopInt().Should().Be((int)sensitivity);
        body.PopBoolean().Should().Be(openingConnection);
        body.Remaining.Should().Be(0);
    }

    /// <summary>
    /// The regression itself: the room's chat mode, bubble width, scroll speed and hear range must
    /// not reach the wire at all, so changing them cannot change a single byte of the packet.
    /// </summary>
    [Fact]
    public void RoomChatModeBubbleAndSpeedAreNoLongerOnTheWire()
    {
        GetGuestRoomResultMessageComposer quiet = Composer(ChatFloodSensitivityType.Extra, true);
        GetGuestRoomResultMessageComposer loud = quiet with
        {
            RoomInfo = quiet.RoomInfo with
            {
                ChatSettings = quiet.RoomInfo.ChatSettings with
                {
                    ChatMode = ChatModeType.Old,
                    BubbleWidth = ChatBubbleWidthType.Thin,
                    ScrollSpeed = ChatScrollSpeedType.Slow,
                    FullHearRange = 30,
                },
            },
        };

        Bytes(loud).Should().Equal(Bytes(quiet));
    }

    private static GetGuestRoomResultMessageComposer Composer(
        ChatFloodSensitivityType sensitivity,
        bool openingConnection
    ) =>
        new()
        {
            EnterRoom = true,
            RoomForward = false,
            IsGroupMember = false,
            AllInRoomMuted = false,
            CanMute = true,
            OpeningConnection = openingConnection,
            RoomInfo = new RoomSnapshot
            {
                RoomId = 11,
                Name = "Welcome Lounge",
                Description = "a lounge",
                OwnerId = (PlayerId)7,
                OwnerName = "absolutezeroo",
                Population = 3,
                DoorMode = RoomDoorModeType.Open,
                PlayersMax = 25,
                TradeType = RoomTradeModeType.Disabled,
                Score = 0,
                Ranking = 0,
                CategoryId = -1,
                Tags = [],
                AllowBlocking = false,
                AllowPets = false,
                AllowPetsEat = false,
                GroupId = null,
                GroupName = null,
                GroupBadge = null,
                PaintWall = string.Empty,
                PaintFloor = string.Empty,
                PaintLandscape = string.Empty,
                LastUpdatedUtc = DateTime.UnixEpoch,
                Password = string.Empty,
                WorldType = "private",
                HideWalls = false,
                WallThickness = RoomThicknessType.Normal,
                FloorThickness = RoomThicknessType.Normal,
                MaxVisitorsLimit = 25,
                ModSettings = new ModSettingsSnapshot
                {
                    WhoCanMute = ModSettingType.Owner,
                    WhoCanKick = ModSettingType.Owner,
                    WhoCanBan = ModSettingType.Owner,
                },
                ChatSettings = new ChatSettingsSnapshot
                {
                    ChatMode = ChatModeType.FreeFlow,
                    BubbleWidth = ChatBubbleWidthType.Normal,
                    ScrollSpeed = ChatScrollSpeedType.Normal,
                    FullHearRange = 14,
                    FloodSensitivity = sensitivity,
                },
            },
        };

    private static byte[] Bytes(GetGuestRoomResultMessageComposer composer) =>
        Revision
            .Serializers[typeof(GetGuestRoomResultMessageComposer)]
            .Serialize(composer)
            .ToArray();

    private static ClientPacket Serialize(GetGuestRoomResultMessageComposer composer)
    {
        byte[] bytes = Bytes(composer);
        byte[] payload = new byte[bytes.Length - 6];
        Array.Copy(bytes, 6, payload, 0, payload.Length);

        return new ClientPacket(0, payload);
    }
}
