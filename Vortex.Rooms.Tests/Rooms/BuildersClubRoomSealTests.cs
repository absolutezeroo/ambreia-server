using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Context;
using Vortex.Database.Entities.Furniture;
using Vortex.Database.Entities.Players;
using Vortex.Database.Entities.Room;
using Vortex.Players;
using Vortex.Primitives.Navigator.Enums;
using Vortex.Primitives.Players.Enums;
using Vortex.Primitives.Rooms.Enums;
using Xunit;

namespace Vortex.Rooms.Tests.Rooms;

/// <summary>
/// When a lapsed Builders Club subscription seals the owner's room.
/// </summary>
/// <remarks>
/// Both halves of the rule are load-bearing and each one alone is a plausible-looking bug: sealing
/// on the subscription alone would hide every free player's rooms, and sealing on the furniture
/// alone would hide a paying builder's. The third case is the one that actually seals, and the
/// fourth is what lets a player fix it — take the Builders Club furniture out and the room opens
/// again, which is why the answer is computed per call rather than stamped on the room.
/// </remarks>
public sealed class BuildersClubRoomSealTests : IDisposable
{
    private const int OWNER = 11;
    private const int ROOM = 21;

    private readonly DbContextOptions<VortexDbContext> _options =
        new DbContextOptionsBuilder<VortexDbContext>()
            .UseInMemoryDatabase($"bc-seal-{Guid.NewGuid():N}")
            .Options;

    public void Dispose()
    {
        using VortexDbContext db = new(_options);
        db.Database.EnsureDeleted();
    }

    [Fact]
    public async Task NoBuildersClubFurniture_IsNeverSealed_HoweverLongTheSubscriptionHasLapsed()
    {
        await SeedAsync(
                hasBuildersClubFurniture: false,
                subscriptionExpiresIn: TimeSpan.FromDays(-30)
            )
            .ConfigureAwait(true);

        (await IsSealedAsync().ConfigureAwait(true)).Should().BeFalse();
    }

    [Fact]
    public async Task BuildersClubFurniture_WithAnActiveSubscription_IsNotSealed()
    {
        await SeedAsync(
                hasBuildersClubFurniture: true,
                subscriptionExpiresIn: TimeSpan.FromDays(30)
            )
            .ConfigureAwait(true);

        (await IsSealedAsync().ConfigureAwait(true)).Should().BeFalse();
    }

    [Fact]
    public async Task BuildersClubFurniture_WithALapsedSubscription_IsSealed()
    {
        await SeedAsync(
                hasBuildersClubFurniture: true,
                subscriptionExpiresIn: TimeSpan.FromDays(-1)
            )
            .ConfigureAwait(true);

        (await IsSealedAsync().ConfigureAwait(true)).Should().BeTrue();
    }

    [Fact]
    public async Task BuildersClubFurniture_WithNoSubscriptionRowAtAll_IsSealed()
    {
        await SeedAsync(hasBuildersClubFurniture: true, subscriptionExpiresIn: null)
            .ConfigureAwait(true);

        (await IsSealedAsync().ConfigureAwait(true)).Should().BeTrue();
    }

    /// <summary>Taking the rented furniture out is the player's own way to reopen the room.</summary>
    [Fact]
    public async Task PickingTheBuildersClubFurnitureUp_UnsealsTheRoom()
    {
        await SeedAsync(
                hasBuildersClubFurniture: true,
                subscriptionExpiresIn: TimeSpan.FromDays(-1)
            )
            .ConfigureAwait(true);

        await using (VortexDbContext db = new(_options))
        {
            FurnitureEntity item = await db.Furnitures.FirstAsync().ConfigureAwait(true);
            item.RoomEntityId = null;
            await db.SaveChangesAsync().ConfigureAwait(true);
        }

        (await IsSealedAsync().ConfigureAwait(true)).Should().BeFalse();
    }

    private Task<bool> IsSealedAsync() =>
        new BuildersClubService(new TestDbContextFactory(_options)).IsRoomSealedAsync(
            ROOM,
            CancellationToken.None
        );

    private async Task SeedAsync(bool hasBuildersClubFurniture, TimeSpan? subscriptionExpiresIn)
    {
        await using VortexDbContext db = new(_options);

        PlayerEntity owner = new()
        {
            Id = OWNER,
            Name = "Builder",
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
        db.Rooms.Add(NewRoom(owner, model));

        if (hasBuildersClubFurniture)
        {
            db.Furnitures.Add(
                new FurnitureEntity
                {
                    PlayerEntityId = OWNER,
                    FurnitureDefinitionEntityId = 1,
                    RoomEntityId = ROOM,
                    IsBuildersClub = true,
                }
            );
        }
        else
        {
            // A room full of ordinary furniture must read exactly like an empty one here.
            db.Furnitures.Add(
                new FurnitureEntity
                {
                    PlayerEntityId = OWNER,
                    FurnitureDefinitionEntityId = 1,
                    RoomEntityId = ROOM,
                    IsBuildersClub = false,
                }
            );
        }

        if (subscriptionExpiresIn is TimeSpan expiresIn)
        {
            db.PlayerSubscriptions.Add(
                new PlayerSubscriptionEntity
                {
                    PlayerEntityId = OWNER,
                    PlayerEntity = owner,
                    SubscriptionType = SubscriptionType.BuildersClub,
                    Level = 1,
                    ExpiresAt = DateTime.UtcNow + expiresIn,
                }
            );
        }

        await db.SaveChangesAsync().ConfigureAwait(true);
    }

    private static RoomEntity NewRoom(PlayerEntity owner, RoomModelEntity model) =>
        new()
        {
            Id = ROOM,
            Name = "Site",
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
            Score = 0,
            IsStaffPick = false,
        };

    private sealed class TestDbContextFactory(DbContextOptions<VortexDbContext> options)
        : IDbContextFactory<VortexDbContext>
    {
        public VortexDbContext CreateDbContext() => new(options);
    }
}
