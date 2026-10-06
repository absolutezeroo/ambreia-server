using System;
using System.Collections.Immutable;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Navigator.Enums;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.Orleans.Snapshots.Room.Settings;
using Vortex.Primitives.Packets;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Protocol.Messages.Outgoing.Roomsettings;
using Vortex.Revisions.Configuration;
using Xunit;
using Rev = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions.Tests.RoomSettings;

/// <summary>
/// The room-settings dialog was serialized against the old r63 layout — an owner block, a password
/// and a guild block this revision's client never reads, byte-wide booleans where it reads 4-byte
/// integers, and a five-field chat block that collapsed to a single flood-sensitivity integer. On
/// top of that the serializer was never registered, so nothing reached the client at all.
/// This test replays the WIN63-202607011411 parser (<c>_SafePkg_2452._SafeCls_3719</c>) read for
/// read.
/// </summary>
public sealed class RoomSettingsDataWireLayoutTests
{
    private static readonly Rev Revision = new(Options.Create(new ProtocolLimitsConfig()));

    [Fact]
    public void Layout_MatchesTheClientParserFieldForField()
    {
        ClientPacket body = Serialize(Settings());

        body.PopInt().Should().Be(42, "roomId");
        body.PopString().Should().Be("HQ", "name");
        body.PopString().Should().Be("desc", "description");
        body.PopInt().Should().Be((int)RoomDoorModeType.Password, "doorMode");
        body.PopInt().Should().Be(3, "categoryId");
        body.PopInt().Should().Be(25, "maximumVisitors");
        body.PopInt().Should().Be(50, "maximumVisitorsLimit");

        body.PopInt().Should().Be(2, "tag count");
        body.PopString().Should().Be("build");
        body.PopString().Should().Be("chat");

        body.PopInt().Should().Be((int)RoomTradeModeType.Everyone, "tradeMode");
        body.PopInt().Should().Be(1, "allowPets");
        body.PopInt().Should().Be(0, "allowFoodConsume");
        body.PopInt().Should().Be(1, "allowWalkThrough");
        body.PopInt().Should().Be(0, "hideWalls");
        body.PopInt().Should().Be((int)RoomThicknessType.Thin, "wallThickness");
        body.PopInt().Should().Be((int)RoomThicknessType.Thick, "floorThickness");
        body.PopInt().Should().Be((int)ChatFloodSensitivityType.Minimal, "chatFloodSensitivity");

        body.PopBoolean().Should().BeFalse("leaveOnDoorTileEnabled");
        body.PopBoolean().Should().BeTrue("idleSleepEnabled");
        body.PopInt().Should().Be(300, "idleSleepTimeoutSeconds");
        body.PopBoolean().Should().BeFalse("idleAutokickEnabled");
        body.PopInt().Should().Be(1800, "idleAutokickTimeoutSeconds");
        body.PopBoolean().Should().BeFalse("muteAllPets");

        body.PopInt().Should().Be((int)ModSettingType.Rights, "whoCanMute");
        body.PopInt().Should().Be((int)ModSettingType.GroupRights, "whoCanKick");
        body.PopInt().Should().Be((int)ModSettingType.Owner, "whoCanBan");

        body.PopBoolean().Should().BeFalse("hiddenByBc");
        body.End.Should().BeTrue();
    }

    /// <summary>
    /// The allow/hide flags are integers compared against 1 on the client, not booleans. Writing
    /// them a byte wide would leave three stray bytes each and shift every following field, so pin
    /// the width by reading straight through to the thicknesses.
    /// </summary>
    [Fact]
    public void Flags_AreFourByteIntegersNotBytes()
    {
        ClientPacket body = Serialize(Settings() with { Tags = [] });

        body.PopInt(); // roomId
        body.PopString(); // name
        body.PopString(); // description
        body.PopInt(); // doorMode
        body.PopInt(); // categoryId
        body.PopInt(); // maximumVisitors
        body.PopInt(); // maximumVisitorsLimit
        body.PopInt().Should().Be(0, "tag count");
        body.PopInt(); // tradeMode

        body.PopInt().Should().Be(1, "allowPets");
        body.PopInt().Should().Be(0, "allowFoodConsume");
        body.PopInt().Should().Be(1, "allowWalkThrough");
        body.PopInt().Should().Be(0, "hideWalls");
        body.PopInt().Should().Be((int)RoomThicknessType.Thin, "wallThickness");
        body.PopInt().Should().Be((int)RoomThicknessType.Thick, "floorThickness");
    }

    /// <summary>
    /// The guild block was the r63 layout's tail. This revision's client has no room for it, so a
    /// guild room must serialize to exactly the same size as a guildless one.
    /// </summary>
    [Fact]
    public void GuildRoom_WritesNoGuildBlock()
    {
        RoomSnapshot guildless = Settings();
        RoomSnapshot guilded = guildless with
        {
            GroupId = 5,
            GroupName = "Pixel Painters",
            GroupBadge = "b0102",
        };

        Serialize(guilded).Remaining.Should().Be(Serialize(guildless).Remaining);
    }

    /// <summary>A composer with no registered serializer is silently dropped on the way out.</summary>
    [Fact]
    public void Composer_IsRegisteredForThisRevision()
    {
        Revision
            .Serializers.ContainsKey(typeof(RoomSettingsDataEventMessageComposer))
            .Should()
            .BeTrue();
    }

    private static RoomSnapshot Settings() =>
        new()
        {
            RoomId = 42,
            Name = "HQ",
            Description = "desc",
            OwnerId = (PlayerId)7,
            OwnerName = "absolutezeroo",
            Population = 0,
            DoorMode = RoomDoorModeType.Password,
            Password = "hunter2",
            PlayersMax = 25,
            MaxVisitorsLimit = 50,
            TradeType = RoomTradeModeType.Everyone,
            Score = 0,
            Ranking = 0,
            CategoryId = 3,
            Tags = ImmutableArray.Create("build", "chat"),
            AllowBlocking = true,
            AllowPets = true,
            AllowPetsEat = false,
            GroupId = null,
            GroupName = null,
            GroupBadge = null,
            PaintWall = string.Empty,
            PaintFloor = string.Empty,
            PaintLandscape = string.Empty,
            WorldType = "model_a",
            HideWalls = false,
            WallThickness = RoomThicknessType.Thin,
            FloorThickness = RoomThicknessType.Thick,
            ModSettings = new ModSettingsSnapshot
            {
                WhoCanMute = ModSettingType.Rights,
                WhoCanKick = ModSettingType.GroupRights,
                WhoCanBan = ModSettingType.Owner,
            },
            ChatSettings = new ChatSettingsSnapshot
            {
                ChatMode = ChatModeType.Old,
                BubbleWidth = ChatBubbleWidthType.Thin,
                ScrollSpeed = ChatScrollSpeedType.Slow,
                FullHearRange = 14,
                FloodSensitivity = ChatFloodSensitivityType.Minimal,
            },
            LastUpdatedUtc = DateTime.UnixEpoch,
        };

    private static ClientPacket Serialize(RoomSnapshot settings)
    {
        byte[] bytes = Revision
            .Serializers[typeof(RoomSettingsDataEventMessageComposer)]
            .Serialize(new RoomSettingsDataEventMessageComposer { Settings = settings })
            .ToArray();

        byte[] payload = new byte[bytes.Length - 6];
        Array.Copy(bytes, 6, payload, 0, payload.Length);

        return new ClientPacket(0, payload);
    }
}
