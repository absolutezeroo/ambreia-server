using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans;
using Vortex.Database.Context;
using Vortex.Primitives.Commerce;
using Vortex.Primitives.Events;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Navigator.Enums;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.Orleans.Snapshots.Room.Settings;
using Vortex.Primitives.Permissions;
using Vortex.Primitives.Pets.Providers;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Avatars;
using Vortex.Primitives.Rooms.Providers;
using Vortex.Rooms.Configuration;
using Vortex.Rooms.Grains;
using Vortex.Rooms.Grains.Systems;
using Vortex.Rooms.Object.Avatars.Player;
using Vortex.Rooms.Tests.Support;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Chat;

/// <summary>
///     Flood control refused every line that arrived inside the interval, which is a rate limit and
///     not flood control. Typing "hey" then "how are you" a second apart had the room swallow the
///     second — the player sees the chat breaking, not a protection working. Staff were gated like
///     anyone else, so a moderator answering a busy room was cut off mid-sentence by the mechanism
///     meant to help them.
/// </summary>
public sealed class ChatFloodTests
{
    private const int ROOM_ID = 91;
    private static readonly PlayerId Talker = new(101);

    /// <summary>Minimal sensitivity is one second, the shortest window and the easiest to trip.</summary>
    private const int IntervalSeconds = 1;

    [Fact]
    public void ABurstWithinTheAllowance_GoesThroughUntouched()
    {
        RoomChatSystem chat = NewChat(allowance: 5);

        for (int line = 1; line <= 5; line++)
        {
            chat.IsFloodGated(Talker, NewAvatar(isModerator: false), out _)
                .Should()
                .BeFalse($"line {line} is still inside a five-line allowance");
        }
    }

    [Fact]
    public void TheLineAfterTheAllowance_IsTheOneRefused()
    {
        RoomChatSystem chat = NewChat(allowance: 5);
        IRoomAvatar avatar = NewAvatar(isModerator: false);

        for (int line = 1; line <= 5; line++)
        {
            _ = chat.IsFloodGated(Talker, avatar, out _);
        }

        chat.IsFloodGated(Talker, avatar, out int secondsRemaining)
            .Should()
            .BeTrue("the sixth line in the window is flooding");

        secondsRemaining.Should().BeGreaterThan(0, "the client is told how long to wait");
    }

    [Fact]
    public void AModerator_IsNeverGatedHoweverFastTheyType()
    {
        RoomChatSystem chat = NewChat(allowance: 5);
        IRoomAvatar staff = NewAvatar(isModerator: true);

        for (int line = 1; line <= 50; line++)
        {
            chat.IsFloodGated(Talker, staff, out _)
                .Should()
                .BeFalse("staff answering a busy room are doing the job the limit protects");
        }
    }

    [Fact]
    public void AnAllowanceOfOne_StillLetsTheFirstLineThrough()
    {
        // The old behaviour in config form. Even at its strictest the first line of a burst is
        // never the one refused.
        RoomChatSystem chat = NewChat(allowance: 1);
        IRoomAvatar avatar = NewAvatar(isModerator: false);

        chat.IsFloodGated(Talker, avatar, out _).Should().BeFalse();
        chat.IsFloodGated(Talker, avatar, out _).Should().BeTrue();
    }

    private static IRoomAvatar NewAvatar(bool isModerator) =>
        new RoomPlayerAvatar
        {
            ObjectId = new RoomObjectId(1),
            PlayerId = Talker,
            IsModerator = isModerator,
        };

    private static RoomChatSystem NewChat(int allowance)
    {
        RoomGrain grain = GrainActivationContext.CreateWithIntegerKey<RoomGrain>(
            ROOM_ID,
            FakeProxy.Create<IDbContextFactory<VortexDbContext>>(_ => null),
            FakeProxy.Create<IFurnitureDefinitionProvider>(_ => null),
            FakeProxy.Create<IStuffDataFactory>(_ => null),
            Options.Create(
                new RoomConfig
                {
                    ChatFloodIntervalSeconds = [IntervalSeconds, IntervalSeconds, IntervalSeconds],
                    ChatFloodAllowance = allowance,
                }
            ),
            NullLogger<IRoomGrain>.Instance,
            FakeProxy.Create<IRoomModelProvider>(_ => null),
            FakeProxy.Create<IRoomItemsProvider>(_ => null),
            FakeProxy.Create<IRoomObjectLogicProvider>(_ => null),
            FakeProxy.Create<IRoomAvatarProvider>(_ => null),
            RoomGrainStubs.NoListeners(),
            FakeProxy.Create<IGrainFactory>(_ => null),
            FakeProxy.Create<IEventPublisher>(_ => null),
            RoomGrainStubs.NeverCancels(),
            FakeProxy.Create<IPermissionService>(_ => null),
            FakeProxy.Create<IVortexMetrics>(_ => null),
            FakeProxy.Create<IRoomModerationStore>(_ => null),
            TestRoomGames.Provider(),
            FakeProxy.Create<ICommerceJournal>(_ => Task.CompletedTask),
            RoomGrainStubs.PassThroughWordFilter()
        );

        grain._state.RoomSnapshot = NewRoomSnapshot();

        return new RoomChatSystem(grain);
    }

    private static RoomSnapshot NewRoomSnapshot() =>
        new()
        {
            RoomId = new RoomId(ROOM_ID),
            Name = "Test room",
            Description = string.Empty,
            OwnerId = new PlayerId(1),
            OwnerName = "Owner",
            Population = 0,
            LastUpdatedUtc = DateTime.UnixEpoch,
            DoorMode = RoomDoorModeType.Open,
            PlayersMax = 25,
            TradeType = RoomTradeModeType.Disabled,
            Score = 0,
            Ranking = 0,
            CategoryId = -1,
            Tags = [],
            AllowBlocking = false,
            AllowPets = true,
            AllowPetsEat = false,
            PaintWall = string.Empty,
            PaintFloor = string.Empty,
            PaintLandscape = string.Empty,
            Password = string.Empty,
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
                FullHearRange = 50,
                FloodSensitivity = ChatFloodSensitivityType.Minimal,
            },
            WorldType = string.Empty,
            HideWalls = false,
            WallThickness = RoomThicknessType.Normal,
            FloorThickness = RoomThicknessType.Normal,
            MaxVisitorsLimit = 25,
        };
}
