using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Database.Context;
using Vortex.Database.Entities.Players;
using Vortex.Database.Entities.Room;
using Vortex.Primitives.Navigator.Enums;
using Vortex.Primitives.Players.Enums;
using Vortex.Primitives.Rooms.Enums;
using Xunit;

namespace Vortex.WebApi.Tests;

/// <summary>
/// The appart gallery and the appart page, through the real routing pipeline.
/// </summary>
/// <remarks>
/// The rule worth a test is the invisible door. A room set to
/// <see cref="RoomDoorModeType.Invisible"/> is meant to be unfindable by anyone who was not told
/// about it, and a web page answering for it walks around the door the client enforces — a leak
/// nobody would see, because the room still works.
/// </remarks>
public sealed class RoomEndpointTests
{
    [Fact]
    public async Task Gallery_LeavesOutAnInvisibleRoom()
    {
        await using WebApiTestFactory factory = new();
        await SeedRoomAsync(factory, 1, "Le grand café", RoomDoorModeType.Open, usersNow: 3);
        await SeedRoomAsync(factory, 2, "Planque", RoomDoorModeType.Invisible, usersNow: 9);

        JsonElement gallery = await GetJsonAsync(factory.Client, "/api/public/rooms");

        Names(gallery.GetProperty("items")).Should().Equal("Le grand café");
        gallery.GetProperty("total").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Gallery_PutsTheBusiestFirst()
    {
        await using WebApiTestFactory factory = new();
        await SeedRoomAsync(factory, 1, "Calme", RoomDoorModeType.Open, usersNow: 2);
        await SeedRoomAsync(factory, 2, "Plein", RoomDoorModeType.Open, usersNow: 31);

        JsonElement gallery = await GetJsonAsync(factory.Client, "/api/public/rooms");

        Names(gallery.GetProperty("items")).Should().Equal("Plein", "Calme");
    }

    [Fact]
    public async Task Room_CarriesTheOwnerAndTheTagsThatAreSet()
    {
        await using WebApiTestFactory factory = new();
        await SeedRoomAsync(factory, 1, "Le grand café", RoomDoorModeType.Open, usersNow: 3);

        JsonElement room = await GetJsonAsync(factory.Client, "/api/public/rooms/1");

        room.GetProperty("ownerName").GetString().Should().Be("Owner1");
        room.GetProperty("tags")
            .EnumerateArray()
            .Select(tag => tag.GetString())
            .Should()
            .Equal("cafe");
    }

    [Fact]
    public async Task Room_SaysWhetherItsDoorIsOpen_AndNothingMoreAboutIt()
    {
        // What habbo.com's `room.html` branches on: a room behind a doorbell or a password gets the
        // restricted screen instead of the full page with an enter button. It is a BOOLEAN, and
        // deliberately — which KIND of door it is would tell a stranger how to prepare for it.
        await using WebApiTestFactory factory = new();
        await SeedRoomAsync(factory, 1, "Le grand café", RoomDoorModeType.Open, usersNow: 3);
        await SeedRoomAsync(factory, 2, "Le club privé", RoomDoorModeType.Password, usersNow: 5);

        JsonElement open = await GetJsonAsync(factory.Client, "/api/public/rooms/1");
        JsonElement shut = await GetJsonAsync(factory.Client, "/api/public/rooms/2");

        open.GetProperty("doorOpen").GetBoolean().Should().BeTrue();
        shut.GetProperty("doorOpen").GetBoolean().Should().BeFalse();

        // And nothing in the payload says "password" rather than "doorbell".
        shut.ToString().Should().NotContainEquivalentOf("password");
    }

    [Fact]
    public async Task Room_IsA404WhenItsDoorIsInvisible()
    {
        await using WebApiTestFactory factory = new();
        await SeedRoomAsync(factory, 2, "Planque", RoomDoorModeType.Invisible, usersNow: 9);

        HttpResponseMessage response = await factory.Client.GetAsync("/api/public/rooms/2");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static async Task SeedRoomAsync(
        WebApiTestFactory factory,
        int roomId,
        string name,
        RoomDoorModeType door,
        int usersNow
    )
    {
        await using VortexDbContext db = await factory.DbContexts.CreateDbContextAsync();

        PlayerEntity owner = new PlayerEntity
        {
            Id = roomId,
            Name = $"Owner{roomId}",
            Figure = "hr-115",
            Gender = AvatarGenderType.Male,
            PlayerStatus = PlayerStatusType.Offline,
            PlayerPerks = PlayerPerkFlags.None,
        };

        RoomModelEntity model = new RoomModelEntity
        {
            Id = roomId + 1000,
            Name = $"model-{roomId}",
            Model = "0",
            DoorX = 0,
            DoorY = 0,
            DoorRotation = Rotation.North,
            Enabled = true,
            Custom = false,
        };

        db.Players.Add(owner);
        db.RoomModels.Add(model);
        db.Rooms.Add(
            new RoomEntity
            {
                Id = roomId,
                Name = name,
                Description = "Ouvert tous les soirs",
                PlayerEntityId = owner.Id,
                DoorMode = door,
                RoomModelEntityId = model.Id,
                UsersNow = usersNow,
                PlayersMax = 50,
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
                Tag1 = "cafe",
                PlayerEntity = owner,
                RoomModelEntity = model,
            }
        );

        await db.SaveChangesAsync();
    }

    private static string[] Names(JsonElement items) =>
        items.EnumerateArray().Select(item => item.GetProperty("name").GetString()!).ToArray();

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string url)
    {
        HttpResponseMessage response = await client.GetAsync(url);

        response.EnsureSuccessStatusCode();

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }
}
