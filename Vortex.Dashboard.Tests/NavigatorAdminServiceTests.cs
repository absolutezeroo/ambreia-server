using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Vortex.Dashboard.API.Admin.Hotel;
using Vortex.Database.Context;
using Vortex.Database.Entities.Navigator;
using Vortex.Database.Entities.Players;
using Vortex.Database.Entities.Room;
using Vortex.Primitives.Navigator;
using Vortex.Primitives.Navigator.Admin;
using Vortex.Primitives.Navigator.Enums;
using Vortex.Primitives.Players.Enums;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Dashboard.Tests;

/// <summary>
/// Covers what <c>NavigatorAdminService</c> is responsible for beyond writing a row: the guards that
/// stop a delete from orphaning the rows pointing at it, the duplicate search code that would make
/// <c>ResolveQueryType</c> a coin toss, the seed that has to be safe to run on a hotel that is
/// already half configured, and — the one that matters most — that every write reloads the provider
/// snapshot, since it is built once at reference-data load and never re-read.
/// </summary>
public sealed class NavigatorAdminServiceTests
{
    private static DbContextOptions<VortexDbContext> NewOptions() =>
        new DbContextOptionsBuilder<VortexDbContext>()
            .UseInMemoryDatabase($"navigator-admin-{Guid.NewGuid():N}")
            .Options;

    private sealed class TestContextFactory(DbContextOptions<VortexDbContext> options)
        : IDbContextFactory<VortexDbContext>
    {
        public VortexDbContext CreateDbContext() => new(options);
    }

    private static NavigatorAdminService NewService(
        DbContextOptions<VortexDbContext> options,
        out List<string> reloads
    )
    {
        List<string> captured = [];
        reloads = captured;

        INavigatorProvider provider = FakeProxy.Create<INavigatorProvider>(call =>
        {
            if (call.Method.Name == nameof(INavigatorProvider.ReloadAsync))
            {
                captured.Add(call.Method.Name);
            }

            return null;
        });

        return new NavigatorAdminService(
            new TestContextFactory(options),
            provider,
            NullLogger<NavigatorAdminService>.Instance
        );
    }

    private static NavigatorContextSpec Context(string code, int order = 0) =>
        new(code, true, NavigatorQueryType.Popular, order);

    [Fact]
    public async Task SeedDefaults_CreatesEveryTabTheClientAsksFor()
    {
        DbContextOptions<VortexDbContext> options = NewOptions();
        NavigatorAdminService service = NewService(options, out List<string> reloads);

        NavigatorAdminResult result = await service.SeedDefaultsAsync(CancellationToken.None);

        result.Success.Should().BeTrue();

        await using VortexDbContext db = new(options);

        List<string> codes = await db
            .NavigatorTopLevelContexts.Select(c => c.SearchCode)
            .ToListAsync();

        codes.Should().BeEquivalentTo(NavigatorSearchCodes.TopLevelViews);
        (await db.NavigatorQuickLinks.CountAsync()).Should().BeGreaterThan(0);
        reloads.Should().ContainSingle();
    }

    [Fact]
    public async Task SeedDefaults_RunTwice_AddsNothingTheSecondTime()
    {
        DbContextOptions<VortexDbContext> options = NewOptions();
        NavigatorAdminService service = NewService(options, out _);

        await service.SeedDefaultsAsync(CancellationToken.None);

        await using (VortexDbContext first = new(options))
        {
            int contexts = await first.NavigatorTopLevelContexts.CountAsync();
            int links = await first.NavigatorQuickLinks.CountAsync();

            NavigatorAdminResult second = await service.SeedDefaultsAsync(CancellationToken.None);

            second.Success.Should().BeTrue();
            // Nothing new was created, which is what makes it safe to run on a configured hotel.
            second.Id.Should().Be(0);

            await using VortexDbContext after = new(options);
            (await after.NavigatorTopLevelContexts.CountAsync()).Should().Be(contexts);
            (await after.NavigatorQuickLinks.CountAsync()).Should().Be(links);
        }
    }

    [Fact]
    public async Task SeedDefaults_FillsInOnlyTheMissingBlocksOfAnExistingTab()
    {
        DbContextOptions<VortexDbContext> options = NewOptions();
        NavigatorAdminService service = NewService(options, out _);

        await using (VortexDbContext seed = new(options))
        {
            seed.NavigatorTopLevelContexts.Add(
                new NavigatorTopLevelContextEntity
                {
                    SearchCode = NavigatorSearchCodes.HotelView,
                    Visible = false,
                    QueryType = NavigatorQueryType.TextSearch,
                    OrderNum = 42,
                }
            );
            await seed.SaveChangesAsync();
        }

        await service.SeedDefaultsAsync(CancellationToken.None);

        await using VortexDbContext db = new(options);

        NavigatorTopLevelContextEntity hotel = await db.NavigatorTopLevelContexts.SingleAsync(c =>
            c.SearchCode == NavigatorSearchCodes.HotelView
        );

        // The operator's own configuration of an existing tab is left exactly as they set it.
        hotel.Visible.Should().BeFalse();
        hotel.QueryType.Should().Be(NavigatorQueryType.TextSearch);
        hotel.OrderNum.Should().Be(42);
        (await db.NavigatorQuickLinks.CountAsync(q => q.TopLevelContextEntityId == hotel.Id))
            .Should()
            .BeGreaterThan(0);
    }

    [Fact]
    public async Task CreateContext_RefusesASearchCodeThatIsAlreadyConfigured()
    {
        DbContextOptions<VortexDbContext> options = NewOptions();
        NavigatorAdminService service = NewService(options, out List<string> reloads);

        await service.CreateContextAsync(
            Context(NavigatorSearchCodes.HotelView),
            CancellationToken.None
        );
        reloads.Clear();

        NavigatorAdminResult duplicate = await service.CreateContextAsync(
            Context(NavigatorSearchCodes.HotelView),
            CancellationToken.None
        );

        duplicate.Success.Should().BeFalse();
        duplicate.ErrorCode.Should().Be("search_code_already_configured");
        // A refused write must not reload: there is nothing new to publish.
        reloads.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteContext_IsRefusedWhileItStillCarriesBlocks()
    {
        DbContextOptions<VortexDbContext> options = NewOptions();
        NavigatorAdminService service = NewService(options, out _);

        NavigatorAdminResult created = await service.CreateContextAsync(
            Context(NavigatorSearchCodes.MyWorldView),
            CancellationToken.None
        );

        await service.CreateQuickLinkAsync(
            new NavigatorQuickLinkSpec(
                created.Id!.Value,
                NavigatorSearchCodes.MyRooms,
                string.Empty,
                string.Empty,
                NavigatorQueryType.MyRooms,
                0,
                false
            ),
            CancellationToken.None
        );

        NavigatorAdminResult refused = await service.DeleteContextAsync(
            created.Id.Value,
            CancellationToken.None
        );

        refused.Success.Should().BeFalse();
        refused.ErrorCode.Should().Be("context_has_quick_links");
    }

    [Fact]
    public async Task CreateQuickLink_RefusesATabThatDoesNotExist()
    {
        DbContextOptions<VortexDbContext> options = NewOptions();
        NavigatorAdminService service = NewService(options, out _);

        NavigatorAdminResult result = await service.CreateQuickLinkAsync(
            new NavigatorQuickLinkSpec(
                404,
                NavigatorSearchCodes.Popular,
                string.Empty,
                string.Empty,
                NavigatorQueryType.Popular,
                0,
                false
            ),
            CancellationToken.None
        );

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("context_not_found");
    }

    [Fact]
    public async Task DeleteFlatCategory_IsRefusedWhileARoomIsFiledUnderIt()
    {
        DbContextOptions<VortexDbContext> options = NewOptions();
        NavigatorAdminService service = NewService(options, out _);

        NavigatorAdminResult created = await service.CreateFlatCategoryAsync(
            new NavigatorFlatCategorySpec("Chat", true, false, null, null, false, 1, 0),
            CancellationToken.None
        );

        await using (VortexDbContext seed = new(options))
        {
            PlayerEntity owner = NewOwner();
            RoomModelEntity model = NewModel();
            seed.Players.Add(owner);
            seed.RoomModels.Add(model);
            await seed.SaveChangesAsync();

            seed.Rooms.Add(NewRoom(created.Id!.Value, owner, model));
            await seed.SaveChangesAsync();
        }

        NavigatorAdminResult refused = await service.DeleteFlatCategoryAsync(
            created.Id!.Value,
            CancellationToken.None
        );

        refused.Success.Should().BeFalse();
        refused.ErrorCode.Should().Be("category_in_use");
    }

    [Fact]
    public async Task EveryAcceptedWrite_ReloadsTheProviderSnapshot()
    {
        DbContextOptions<VortexDbContext> options = NewOptions();
        NavigatorAdminService service = NewService(options, out List<string> reloads);

        NavigatorAdminResult context = await service.CreateContextAsync(
            Context(NavigatorSearchCodes.OfficialView),
            CancellationToken.None
        );
        await service.UpdateContextAsync(
            context.Id!.Value,
            Context(NavigatorSearchCodes.OfficialView, order: 3),
            CancellationToken.None
        );
        NavigatorAdminResult category = await service.CreateEventCategoryAsync(
            new NavigatorEventCategorySpec("Party", true),
            CancellationToken.None
        );
        await service.DeleteEventCategoryAsync(category.Id!.Value, CancellationToken.None);

        // Create, update, create, delete: four accepted writes, four reloads. A missed one leaves
        // the live navigator serving the previous snapshot until a restart.
        reloads.Should().HaveCount(4);
    }

    // RoomEntity requires its owner and model navigations, so the guard's fixture is a small graph
    // rather than a single row.
    private static PlayerEntity NewOwner() =>
        new()
        {
            Name = "owner",
            Figure = "hd-180-1",
            Gender = AvatarGenderType.Male,
            PlayerStatus = PlayerStatusType.Offline,
            PlayerPerks = PlayerPerkFlags.None,
        };

    private static RoomModelEntity NewModel() =>
        new()
        {
            Name = "model_a",
            Model = "x",
            DoorX = 0,
            DoorY = 0,
            DoorRotation = Rotation.North,
            Enabled = true,
            Custom = false,
        };

    private static RoomEntity NewRoom(int categoryId, PlayerEntity owner, RoomModelEntity model) =>
        new()
        {
            Name = "test room",
            PlayerEntityId = owner.Id,
            PlayerEntity = owner,
            DoorMode = RoomDoorModeType.Open,
            RoomModelEntityId = model.Id,
            RoomModelEntity = model,
            Score = 0,
            IsStaffPick = false,
            UsersNow = 0,
            PlayersMax = 25,
            PaintWall = string.Empty,
            PaintFloor = string.Empty,
            PaintLandscape = string.Empty,
            WallHeight = -1,
            HideWalls = false,
            ThicknessWall = RoomThicknessType.Normal,
            ThicknessFloor = RoomThicknessType.Normal,
            AllowBlocking = true,
            AllowPets = true,
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
            NavigatorCategoryEntityId = categoryId,
        };
}
