using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans;
using Vortex.Database.Context;
using Vortex.Database.Entities.Navigator;
using Vortex.Database.Entities.Players;
using Vortex.Database.Entities.Room;
using Vortex.Events.Registry;
using Vortex.Logging;
using Vortex.Primitives;
using Vortex.Primitives.Events;
using Vortex.Primitives.Navigator.Enums;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Enums;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Server.Grains;
using Vortex.Rooms.Configuration;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Rooms;

/// <summary>
/// What CreateFlat is allowed to ask for.
/// </summary>
/// <remarks>
/// Nothing, was the answer: the handler forwarded the packet's five fields untouched and the service
/// validated none of them. No quota — and the protocol announces one, since
/// CanCreateRoomMessageComposer carries a RoomLimit the navigator screen computes and the creation
/// path never read. No ceiling on maxPlayers, which is worse than it sounds because the door reads
/// "PlayersMax > 0 && avatars >= PlayersMax", so zero turns the limit off rather than setting it
/// low. And a category id straight off the wire, reaching the insert and failing there on a foreign
/// key.
/// </remarks>
public sealed class RoomCreationGuardsTests : IDisposable
{
    private const int PLAYER = 7;
    private const int MAX_ROOMS = 2;
    private const string MODEL = "model_a";

    private readonly DbContextOptions<VortexDbContext> _options =
        new DbContextOptionsBuilder<VortexDbContext>()
            .UseInMemoryDatabase($"room-create-{Guid.NewGuid():N}")
            .Options;

    public void Dispose()
    {
        using VortexDbContext db = new(_options);
        db.Database.EnsureDeleted();
    }

    private async Task SeedAsync(int roomsAlreadyOwned = 0, int categoryId = 0)
    {
        await using VortexDbContext db = new(_options);

        PlayerEntity player = new()
        {
            Id = PLAYER,
            Name = "Owner",
            Figure = "hd-180-1",
            Gender = AvatarGenderType.Male,
            PlayerStatus = PlayerStatusType.Online,
            PlayerPerks = PlayerPerkFlags.None,
        };

        RoomModelEntity model = new()
        {
            Id = 1,
            Name = MODEL,
            Model = "x",
            DoorX = 0,
            DoorY = 0,
            DoorRotation = Rotation.South,
            Enabled = true,
            Custom = false,
        };

        db.Players.Add(player);
        db.RoomModels.Add(model);

        if (categoryId > 0)
        {
            db.NavigatorFlatCategories.Add(
                new NavigatorFlatCategoryEntity
                {
                    Id = categoryId,
                    Name = "Chat",
                    Visible = true,
                    Automatic = false,
                    StaffOnly = false,
                    MinRank = 0,
                    OrderNum = 1,
                }
            );
        }

        for (int i = 0; i < roomsAlreadyOwned; i++)
        {
            db.Rooms.Add(NewRoom($"existing {i}", player, model));
        }

        await db.SaveChangesAsync();
    }

    private static RoomEntity NewRoom(string name, PlayerEntity player, RoomModelEntity model) =>
        new()
        {
            Name = name,
            Description = string.Empty,
            PlayerEntityId = PLAYER,
            PlayerEntity = player,
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

    private Task<(RoomId RoomId, string Name)> CreateAsync(
        int maxPlayers = 25,
        int categoryId = 0
    ) =>
        BuildService()
            .CreateRoomAsync(
                "A room",
                string.Empty,
                MODEL,
                categoryId,
                maxPlayers,
                RoomTradeModeType.Disabled,
                new PlayerId(PLAYER),
                CancellationToken.None
            );

    private async Task<int> RoomCountAsync()
    {
        await using VortexDbContext db = new(_options);

        return await db.Rooms.CountAsync(r => r.PlayerEntityId == PLAYER);
    }

    /// <summary>The control: within the quota, a room is created.</summary>
    [Fact]
    public async Task UnderTheQuota_ARoomIsCreated()
    {
        await SeedAsync();

        (RoomId roomId, _) = await CreateAsync().ConfigureAwait(true);

        roomId.Value.Should().BePositive();
        (await RoomCountAsync()).Should().Be(1);
    }

    /// <summary>
    /// The limit the protocol already promised. Without it, ten packets a second is thirty-six
    /// thousand rows an hour and nothing ever says no.
    /// </summary>
    [Fact]
    public async Task AtTheQuota_CreationIsRefused()
    {
        await SeedAsync(roomsAlreadyOwned: MAX_ROOMS);

        Func<Task> create = () => CreateAsync();

        await create
            .Should()
            .ThrowAsync<VortexException>()
            .Where(e => e.ErrorCode == VortexErrorCodeEnum.RoomLimitReached)
            .ConfigureAwait(true);

        (await RoomCountAsync()).Should().Be(MAX_ROOMS, "and nothing was written");
    }

    /// <summary>
    /// Zero does not mean "no limit" here — the door reads PlayersMax &gt; 0 — so a room asking for
    /// it must not be created with the limit switched off.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public async Task MaxPlayersIsClampedIntoWhatADoorCanEnforce(int asked)
    {
        await SeedAsync();

        await CreateAsync(maxPlayers: asked).ConfigureAwait(true);

        await using VortexDbContext db = new(_options);
        RoomEntity created = await db.Rooms.SingleAsync(r => r.Name == "A room");

        created.PlayersMax.Should().BeInRange(1, RoomsConfig.MaxPlayersCeiling);
    }

    [Fact]
    public async Task AnUnknownCategory_IsRefusedRatherThanLeftToTheForeignKey()
    {
        await SeedAsync();

        Func<Task> create = () => CreateAsync(categoryId: 999);

        await create
            .Should()
            .ThrowAsync<VortexException>()
            .Where(e => e.ErrorCode == VortexErrorCodeEnum.NavigatorCategoryNotFound)
            .ConfigureAwait(true);

        (await RoomCountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AKnownCategory_IsAccepted()
    {
        await SeedAsync(categoryId: 5);

        await CreateAsync(categoryId: 5).ConfigureAwait(true);

        (await RoomCountAsync()).Should().Be(1);
    }

    private RoomService BuildService() =>
        new(
            NullLogger<IRoomService>.Instance,
            Options.Create(new RoomConfig()),
            FakeProxy.Create<ISessionGateway>(_ => null),
            BuildGrainFactory(),
            new TestDbContextFactory(_options),
            FakeProxy.Create<IRoomModerationStore>(_ => null),
            FakeProxy.Create<IRoomAdvertisementService>(_ => null),
            FakeProxy.Create<IEventPublisher>(_ => Task.CompletedTask),
            FakeProxy.Create<ICancellableEventPublisher>(_ =>
                Task.FromResult(new EventContext { CorrelationId = string.Empty, Cancel = false })
            )
        );

    /// <summary>The room limit is admin-tunable, so it comes from the config grain, not a constant.</summary>
    private IGrainFactory BuildGrainFactory()
    {
        IServerConfigGrain config = FakeProxy.Create<IServerConfigGrain>(call =>
            call.Method.Name == nameof(IServerConfigGrain.GetIntAsync)
                ? Task.FromResult(MAX_ROOMS)
                : null
        );

        return FakeProxy.Create<IGrainFactory>(call =>
            call.Method.IsGenericMethod
            && call.Method.GetGenericArguments()[0] == typeof(IServerConfigGrain)
                ? config
                : null
        );
    }

    private sealed class TestDbContextFactory(DbContextOptions<VortexDbContext> options)
        : IDbContextFactory<VortexDbContext>
    {
        public VortexDbContext CreateDbContext() => new(options);
    }
}
