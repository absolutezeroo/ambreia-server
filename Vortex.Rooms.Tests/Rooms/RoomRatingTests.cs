using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans;
using Vortex.Database.Context;
using Vortex.Database.Entities.Players;
using Vortex.Database.Entities.Room;
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
using Vortex.Primitives.Players.Enums;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Primitives.Rooms.Object.Furniture;
using Vortex.Primitives.Rooms.Providers;
using Vortex.Primitives.Sound.Providers;
using Vortex.Rooms.Configuration;
using Vortex.Rooms.Grains;
using Vortex.Rooms.Tests.Support;
using Vortex.Rooms.Wired.Logs;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Rooms;

/// <summary>
/// The like button is the server's to raise and lower.
/// </summary>
/// <remarks>
/// <para>
/// The client shows it off <c>canRate</c> alone and never withdraws it by itself — rateRoom() sends
/// and forgets — so both ends of the round trip are the server's responsibility. The entry payload
/// used to answer <c>Rating = 0, CanRate = false</c> for every room in the hotel, which hid the
/// button everywhere and showed a score of zero next to it.
/// </para>
/// <para>
/// The pair that matters is <see cref="RoomGrain.CanRateAsync" /> and
/// <see cref="RoomGrain.RateRoomAsync" /> agreeing: whatever the first offers, the second has to
/// accept, or the button is there to be clicked into a silent refusal.
/// </para>
/// </remarks>
public sealed class RoomRatingTests : IDisposable
{
    private const int ROOM_ID = 55;
    private const int OWNER = 7;
    private const int VISITOR = 9;

    private readonly DbContextOptions<VortexDbContext> _options =
        new DbContextOptionsBuilder<VortexDbContext>()
            .UseInMemoryDatabase($"room-rating-{Guid.NewGuid():N}")
            .Options;

    public void Dispose()
    {
        using VortexDbContext db = new(_options);
        db.Database.EnsureDeleted();
    }

    [Fact]
    public async Task TheOwner_IsNeverOfferedTheButton()
    {
        await SeedAsync(score: 12);
        RoomGrain grain = NewGrain(score: 12);

        bool canRate = await grain
            .CanRateAsync(new PlayerId(OWNER), CancellationToken.None)
            .ConfigureAwait(true);

        canRate.Should().BeFalse("a room cannot be liked by whoever owns it");
    }

    [Fact]
    public async Task AVisitorWhoHasNotVoted_IsOfferedTheButton()
    {
        await SeedAsync(score: 12);
        RoomGrain grain = NewGrain(score: 12);

        bool canRate = await grain
            .CanRateAsync(new PlayerId(VISITOR), CancellationToken.None)
            .ConfigureAwait(true);

        canRate.Should().BeTrue();
    }

    /// <summary>
    /// The vote answers with the room's new score, because the caller has to send it back: the
    /// client's displayed rating only ever moves when a RoomRating arrives.
    /// </summary>
    [Fact]
    public async Task AVoteReturnsTheNewScore_AndClosesTheButtonBehindIt()
    {
        await SeedAsync(score: 12);
        RoomGrain grain = NewGrain(score: 12);

        int? newScore = await grain
            .RateRoomAsync(new PlayerId(VISITOR), 1, CancellationToken.None)
            .ConfigureAwait(true);

        newScore.Should().Be(13);

        bool canRateAgain = await grain
            .CanRateAsync(new PlayerId(VISITOR), CancellationToken.None)
            .ConfigureAwait(true);

        canRateAgain.Should().BeFalse("one vote per player per room");
    }

    /// <summary>
    /// A refused vote is null rather than a score, so the caller can tell "nothing happened" from
    /// "the score is now zero" and stays quiet instead of answering with a fresh RoomRating.
    /// </summary>
    [Fact]
    public async Task ASecondVote_IsRefusedWithoutMovingTheScore()
    {
        await SeedAsync(score: 12);
        RoomGrain grain = NewGrain(score: 12);

        await grain
            .RateRoomAsync(new PlayerId(VISITOR), 1, CancellationToken.None)
            .ConfigureAwait(true);

        int? second = await grain
            .RateRoomAsync(new PlayerId(VISITOR), 1, CancellationToken.None)
            .ConfigureAwait(true);

        second.Should().BeNull();

        await using VortexDbContext db = new(_options);
        RoomEntity room = await db.Rooms.FirstAsync(r => r.Id == ROOM_ID).ConfigureAwait(true);

        room.Score.Should().Be(13, "the refused vote must not have counted");
    }

    [Fact]
    public async Task TheOwnersVote_IsRefused()
    {
        await SeedAsync(score: 12);
        RoomGrain grain = NewGrain(score: 12);

        int? result = await grain
            .RateRoomAsync(new PlayerId(OWNER), 1, CancellationToken.None)
            .ConfigureAwait(true);

        result.Should().BeNull();
    }

    private async Task SeedAsync(int score)
    {
        await using VortexDbContext db = new(_options);

        PlayerEntity owner = new()
        {
            Id = OWNER,
            Name = "Owner",
            Figure = "hd-180-1",
            Gender = AvatarGenderType.Male,
            PlayerStatus = PlayerStatusType.Online,
            PlayerPerks = PlayerPerkFlags.None,
        };

        RoomModelEntity model = new()
        {
            Id = 1,
            Name = "model_a",
            Model = "x",
            DoorX = 0,
            DoorY = 0,
            DoorRotation = Rotation.South,
            Enabled = true,
            Custom = false,
        };

        db.Players.Add(owner);
        db.RoomModels.Add(model);
        db.Rooms.Add(
            new RoomEntity
            {
                Id = ROOM_ID,
                Name = "A room",
                Description = string.Empty,
                PlayerEntityId = OWNER,
                PlayerEntity = owner,
                RoomModelEntityId = 1,
                RoomModelEntity = model,
                DoorMode = RoomDoorModeType.Open,
                UsersNow = 0,
                PlayersMax = 25,
                TradeType = RoomTradeModeType.Disabled,
                WallHeight = -1,
                HideWalls = false,
                ThicknessWall = RoomThicknessType.Normal,
                ThicknessFloor = RoomThicknessType.Normal,
                AllowBlocking = false,
                AllowPets = true,
                AllowPetsEat = false,
                MuteType = ModSettingType.Owner,
                KickType = ModSettingType.Owner,
                BanType = ModSettingType.Owner,
                ChatModeType = ChatModeType.FreeFlow,
                ChatBubbleType = ChatBubbleWidthType.Normal,
                ChatSpeedType = ChatScrollSpeedType.Normal,
                ChatFloodType = ChatFloodSensitivityType.Minimal,
                ChatDistance = 50,
                Score = score,
                IsStaffPick = false,
            }
        );

        await db.SaveChangesAsync().ConfigureAwait(true);
    }

    private RoomGrain NewGrain(int score)
    {
        RoomGrain grain = GrainActivationContext.CreateWithIntegerKey<RoomGrain>(
            ROOM_ID,
            new TestDbContextFactory(_options),
            FakeProxy.Create<IFurnitureDefinitionProvider>(_ => null),
            FakeProxy.Create<IStuffDataFactory>(_ => null),
            Options.Create(new RoomConfig()),
            NullLogger<IRoomGrain>.Instance,
            FakeProxy.Create<IRoomModelProvider>(_ => null),
            FakeProxy.Create<IRoomItemsProvider>(_ => null),
            FakeProxy.Create<IRoomObjectLogicProvider>(_ => null),
            FakeProxy.Create<IRoomAvatarProvider>(_ => null),
            FakeProxy.Create<IRoomWiredVariablesProvider>(_ => null),
            RoomGrainStubs.NoListeners(),
            FakeProxy.Create<IGrainFactory>(_ => null),
            FakeProxy.Create<IEventPublisher>(_ => Task.CompletedTask),
            RoomGrainStubs.NeverCancels(),
            FakeProxy.Create<IPermissionService>(_ => null),
            FakeProxy.Create<IVortexMetrics>(_ => null),
            FakeProxy.Create<IRoomModerationStore>(_ => null),
            FakeProxy.Create<IPetLevelProvider>(_ => null),
            FakeProxy.Create<IPetCommandProvider>(_ => null),
            FakeProxy.Create<IPetVocalProvider>(_ => null),
            new RoomWiredLogChannel(),
            FakeProxy.Create<ISongProvider>(_ => null),
            TestRoomGames.Provider(),
            FakeProxy.Create<ICommerceJournal>(_ => Task.CompletedTask),
            RoomGrainStubs.PassThroughWordFilter()
        );

        grain._state.RoomSnapshot = NewRoomSnapshot(score);

        return grain;
    }

    private static RoomSnapshot NewRoomSnapshot(int score) =>
        new()
        {
            RoomId = new RoomId(ROOM_ID),
            Name = "A room",
            Description = string.Empty,
            OwnerId = new PlayerId(OWNER),
            OwnerName = "Owner",
            Population = 0,
            LastUpdatedUtc = DateTime.UnixEpoch,
            DoorMode = RoomDoorModeType.Open,
            PlayersMax = 25,
            TradeType = RoomTradeModeType.Disabled,
            Score = score,
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

    private sealed class TestDbContextFactory(DbContextOptions<VortexDbContext> options)
        : IDbContextFactory<VortexDbContext>
    {
        public VortexDbContext CreateDbContext() => new(options);
    }
}
