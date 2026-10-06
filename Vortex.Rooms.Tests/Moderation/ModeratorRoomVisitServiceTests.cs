using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Context;
using Vortex.Database.Entities.Players;
using Vortex.Database.Entities.Room;
using Vortex.Primitives;
using Vortex.Primitives.Moderation;
using Vortex.Primitives.Navigator.Enums;
using Vortex.Primitives.Players.Enums;
using Vortex.Primitives.Rooms.Enums;
using Xunit;

namespace Vortex.Rooms.Tests.Moderation;

/// <summary>
/// The mod tool's room-visits list reads the append-only <c>room_entry_logs</c>. Two things it has
/// to get right: newest visit first (a moderator opens this to see where someone just was), and a
/// room name on every row even though a visit row only stores the id.
/// </summary>
public sealed class ModeratorRoomVisitServiceTests
{
    private static DbContextOptions<VortexDbContext> NewOptions() =>
        new DbContextOptionsBuilder<VortexDbContext>()
            .UseInMemoryDatabase($"room-visits-{Guid.NewGuid():N}")
            .Options;

    private static PlayerEntity NewPlayer(int id, string name) =>
        new()
        {
            Id = id,
            Name = name,
            Figure = "hd-180-1",
            Gender = AvatarGenderType.Male,
            PlayerStatus = PlayerStatusType.Offline,
            PlayerPerks = PlayerPerkFlags.None,
        };

    private static RoomEntity NewRoom(
        int id,
        string name,
        PlayerEntity owner,
        RoomModelEntity model
    ) =>
        new()
        {
            Id = id,
            Name = name,
            PlayerEntityId = owner.Id,
            DoorMode = RoomDoorModeType.Open,
            RoomModelEntityId = model.Id,
            UsersNow = 0,
            PlayersMax = 25,
            PaintWall = string.Empty,
            PaintFloor = string.Empty,
            PaintLandscape = string.Empty,
            WallHeight = -1,
            HideWalls = false,
            ThicknessWall = RoomThicknessType.Normal,
            ThicknessFloor = RoomThicknessType.Normal,
            AllowBlocking = false,
            AllowPets = false,
            AllowPetsEat = false,
            TradeType = RoomTradeModeType.Disabled,
            MuteType = ModSettingType.Owner,
            KickType = ModSettingType.Owner,
            BanType = ModSettingType.Owner,
            ChatModeType = ChatModeType.FreeFlow,
            ChatBubbleType = ChatBubbleWidthType.Normal,
            ChatSpeedType = ChatScrollSpeedType.Normal,
            ChatFloodType = ChatFloodSensitivityType.Minimal,
            ChatDistance = 50,
            Score = 0,
            PlayerEntity = owner,
            RoomModelEntity = model,
        };

    private static async Task<DbContextOptions<VortexDbContext>> SeedAsync(
        params (int RoomId, DateTime EnteredAt)[] visits
    )
    {
        DbContextOptions<VortexDbContext> options = NewOptions();

        await using VortexDbContext dbCtx = new(options);

        PlayerEntity owner = NewPlayer(1, "Owner");
        PlayerEntity visitor = NewPlayer(42, "Bob");
        RoomModelEntity model = new()
        {
            Id = 1,
            Name = "model-a",
            Model = "0",
            DoorX = 0,
            DoorY = 0,
            DoorRotation = Rotation.North,
            Enabled = true,
            Custom = false,
        };

        dbCtx.Players.Add(owner);
        dbCtx.Players.Add(visitor);
        dbCtx.RoomModels.Add(model);
        dbCtx.Rooms.Add(NewRoom(1, "Lobby", owner, model));
        dbCtx.Rooms.Add(NewRoom(2, "Cafe", owner, model));

        foreach ((int roomId, DateTime enteredAt) in visits)
        {
            dbCtx.RoomEntryLogs.Add(
                new RoomEntryLogEntity
                {
                    RoomEntityId = roomId,
                    PlayerEntityId = 42,
                    CreatedAt = enteredAt,
                }
            );
        }

        await dbCtx.SaveChangesAsync();

        return options;
    }

    private static ModeratorRoomVisitService CreateService(
        DbContextOptions<VortexDbContext> options
    ) => new(new SingleOptionsFactory(options));

    [Fact]
    public async Task GetUserRoomVisits_ReturnsTheMostRecentVisitFirst()
    {
        DbContextOptions<VortexDbContext> options = await SeedAsync(
            (1, new DateTime(2026, 8, 6, 9, 15, 0, DateTimeKind.Utc)),
            (2, new DateTime(2026, 8, 6, 13, 45, 0, DateTimeKind.Utc))
        );

        RoomVisitHistorySnapshot history = await CreateService(options)
            .GetUserRoomVisitsAsync(42, 50, CancellationToken.None);

        history.UserId.Should().Be(42);
        history.UserName.Should().Be("Bob");
        history.Visits.Should().HaveCount(2);

        history.Visits[0].RoomId.Should().Be(2);
        history.Visits[0].RoomName.Should().Be("Cafe");
        history.Visits[0].EnterHour.Should().Be(13);
        history.Visits[0].EnterMinute.Should().Be(45);

        history.Visits[1].RoomId.Should().Be(1);
        history.Visits[1].RoomName.Should().Be("Lobby");
    }

    [Fact]
    public async Task GetUserRoomVisits_HonoursTheLimit()
    {
        List<(int, DateTime)> visits = [];

        for (int i = 0; i < 10; i++)
        {
            visits.Add((1, new DateTime(2026, 8, 6, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i)));
        }

        DbContextOptions<VortexDbContext> options = await SeedAsync([.. visits]);

        RoomVisitHistorySnapshot history = await CreateService(options)
            .GetUserRoomVisitsAsync(42, 3, CancellationToken.None);

        history.Visits.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetUserRoomVisits_ReturnsAnEmptyHistoryForAnUnknownPlayer()
    {
        DbContextOptions<VortexDbContext> options = await SeedAsync();

        RoomVisitHistorySnapshot history = await CreateService(options)
            .GetUserRoomVisitsAsync(9999, 50, CancellationToken.None);

        history.UserName.Should().BeEmpty();
        history.Visits.Should().BeEmpty();
    }

    private sealed class SingleOptionsFactory(DbContextOptions<VortexDbContext> options)
        : IDbContextFactory<VortexDbContext>
    {
        public VortexDbContext CreateDbContext() => new(options);
    }
}
