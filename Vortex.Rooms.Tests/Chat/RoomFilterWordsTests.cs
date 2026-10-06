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
using Vortex.Primitives.Rooms.Providers;
using Vortex.Rooms.Configuration;
using Vortex.Rooms.Grains;
using Vortex.Rooms.Grains.Systems;
using Vortex.Rooms.Tests.Support;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Chat;

/// <summary>
/// The room's own word filter. Both of its handlers were no-ops, so the filter tab of the room
/// settings dialog showed nothing, saved nothing, and no word was ever masked.
/// </summary>
/// <remarks>
/// Whole words only, deliberately: a room that bans "ass" should not mangle "class". An owner who
/// wants the substring can ban it on its own, and the alternative silently rewrites innocent chat
/// in a way nobody can debug from the dialog.
/// </remarks>
public sealed class RoomFilterWordsTests
{
    private const int ROOM_ID = 92;

    [Fact]
    public void AFilteredWord_IsMaskedWhereverItSitsInTheLine()
    {
        RoomChatSystem chat = NewChat("swear");

        chat.ApplyFilterWords("swear").Should().Be("bobba");
        chat.ApplyFilterWords("do not swear here").Should().Be("do not bobba here");
        chat.ApplyFilterWords("swear swear").Should().Be("bobba bobba");
    }

    [Fact]
    public void MatchingIgnoresCase()
    {
        RoomChatSystem chat = NewChat("Secret");

        chat.ApplyFilterWords("the SECRET is out").Should().Be("the bobba is out");
    }

    [Fact]
    public void AWordItMerelyContains_IsLeftAlone()
    {
        RoomChatSystem chat = NewChat("ass");

        chat.ApplyFilterWords("what a class").Should().Be("what a class");
    }

    [Fact]
    public void AnEmptyFilter_ReturnsTheSameStringUntouched()
    {
        RoomChatSystem chat = NewChat();

        chat.ApplyFilterWords("nothing to do here").Should().Be("nothing to do here");
    }

    /// <summary>
    /// The replacement is <c>room.filter.replacement</c>, cached on the room rather than read per
    /// line. The default only stands in for a read that has not happened.
    /// </summary>
    [Fact]
    public void TheReplacementIsWhateverTheRoomWasToldItIs()
    {
        RoomChatSystem chat = NewChatMasking("***", "swear");

        chat.ApplyFilterWords("do not swear here").Should().Be("do not *** here");
    }

    private static RoomChatSystem NewChat(params string[] words) => NewChatMasking(null, words);

    private static RoomChatSystem NewChatMasking(string? replacement, params string[] words)
    {
        RoomGrain grain = GrainActivationContext.CreateWithIntegerKey<RoomGrain>(
            ROOM_ID,
            FakeProxy.Create<IDbContextFactory<VortexDbContext>>(_ => null),
            FakeProxy.Create<IFurnitureDefinitionProvider>(_ => null),
            FakeProxy.Create<IStuffDataFactory>(_ => null),
            Options.Create(new RoomConfig()),
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
            FakeProxy.Create<IPetLevelProvider>(_ => null),
            FakeProxy.Create<IPetCommandProvider>(_ => null),
            FakeProxy.Create<IPetVocalProvider>(_ => null),
            TestRoomGames.Provider(),
            FakeProxy.Create<ICommerceJournal>(_ => Task.CompletedTask),
            RoomGrainStubs.PassThroughWordFilter()
        );

        grain._state.RoomSnapshot = NewRoomSnapshot();

        foreach (string word in words)
        {
            grain._state.FilterWords.Add(word);
        }

        if (replacement is not null)
        {
            grain._state.FilterReplacement = replacement;
        }

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
            StaffPick = false,
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
