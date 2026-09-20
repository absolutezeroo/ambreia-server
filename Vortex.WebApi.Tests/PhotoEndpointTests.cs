using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Database.Context;
using Vortex.Database.Entities.Camera;
using Vortex.Database.Entities.Players;
using Vortex.Primitives.Players.Enums;
using Vortex.Primitives.Rooms.Enums;
using Xunit;

namespace Vortex.WebApi.Tests;

/// <summary>
/// The photo gallery and a player's photos, through the real routing pipeline.
/// </summary>
/// <remarks>
/// <para>
/// One rule is worth every test here, and it is a disclosure rule: a row leaves these routes only
/// when the player PUBLISHED it, has not deleted it, and is not hiding their profile. Publishing is
/// a deliberate act that costs credits, so an unpublished photo is private in the strongest sense —
/// and unlike the invisible door beside it, a leak would be invisible to the person leaked, because
/// their photo still works exactly as before.
/// </para>
/// <para>
/// The owner's <c>ProfileVisible</c> is the condition most likely to be dropped in a later edit:
/// it lives on a different table from the photo and it is not what "published" means. A player who
/// has hidden their profile has said they do not want to be browsed, and a gallery listing their
/// photos under their name is the same disclosure through another door.
/// </para>
/// </remarks>
public sealed class PhotoEndpointTests
{
    [Fact]
    public async Task Gallery_ShowsOnlyPublishedPhotos()
    {
        await using WebApiTestFactory factory = new();
        await SeedAsync(
            factory,
            1,
            "Photographe",
            visible: true,
            keys: [("published", Published), ("private", null)]
        );

        JsonElement gallery = await GetJsonAsync(factory, "/api/public/photos");

        Keys(gallery.GetProperty("items")).Should().Equal("published");
        gallery.GetProperty("total").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Gallery_LeavesOutADeletedPhoto()
    {
        await using WebApiTestFactory factory = new();
        await SeedAsync(factory, 1, "Photographe", visible: true, keys: [("gone", Published)]);

        await using (VortexDbContext db = await factory.DbContexts.CreateDbContextAsync())
        {
            CameraPhotoEntity photo = db.CameraPhotos.Single(p => p.PhotoKey == "gone");
            photo.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        JsonElement gallery = await GetJsonAsync(factory, "/api/public/photos");

        gallery.GetProperty("total").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Gallery_LeavesOutAPhotoWhoseOwnerHidesTheirProfile()
    {
        await using WebApiTestFactory factory = new();
        await SeedAsync(factory, 1, "Ouvert", visible: true, keys: [("seen", Published)]);
        await SeedAsync(factory, 2, "Discret", visible: false, keys: [("hidden", Published)]);

        JsonElement gallery = await GetJsonAsync(factory, "/api/public/photos");

        Keys(gallery.GetProperty("items")).Should().Equal("seen");
    }

    [Fact]
    public async Task Gallery_PutsTheNewestFirst()
    {
        await using WebApiTestFactory factory = new();
        await SeedAsync(
            factory,
            1,
            "Photographe",
            visible: true,
            keys:
            [
                ("older", new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)),
                ("newer", new DateTime(2026, 6, 2, 0, 0, 0, DateTimeKind.Utc)),
            ]
        );

        JsonElement gallery = await GetJsonAsync(factory, "/api/public/photos");

        Keys(gallery.GetProperty("items")).Should().Equal("newer", "older");
    }

    [Fact]
    public async Task PlayerPhotos_AnswersOnlyThatPlayers()
    {
        await using WebApiTestFactory factory = new();
        await SeedAsync(factory, 1, "Un", visible: true, keys: [("un", Published)]);
        await SeedAsync(factory, 2, "Deux", visible: true, keys: [("deux", Published)]);

        JsonElement mine = await GetJsonAsync(factory, "/api/public/users/1/photos");

        Keys(mine).Should().Equal("un");
    }

    /// <summary>
    /// A junk id is an EMPTY LIST, not a 404 — the same answer as a player with no photos, so the
    /// section cannot be used to test whether an id exists.
    /// </summary>
    [Fact]
    public async Task PlayerPhotos_AnswersEmptyForAnIdThatIsNotANumber()
    {
        await using WebApiTestFactory factory = new();

        JsonElement answer = await GetJsonAsync(factory, "/api/public/users/pas-un-id/photos");

        answer.EnumerateArray().Should().BeEmpty();
    }

    private static readonly DateTime Published = new(2026, 3, 4, 12, 0, 0, DateTimeKind.Utc);

    private static string[] Keys(JsonElement items) =>
        [.. items.EnumerateArray().Select(item => item.GetProperty("key").GetString() ?? "")];

    private static async Task<JsonElement> GetJsonAsync(WebApiTestFactory factory, string url)
    {
        string body = await factory.Client.GetStringAsync(url);

        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static async Task SeedAsync(
        WebApiTestFactory factory,
        int playerId,
        string name,
        bool visible,
        (string Key, DateTime? PublishedAt)[] keys
    )
    {
        await using VortexDbContext db = await factory.DbContexts.CreateDbContextAsync();

        PlayerEntity player = new()
        {
            Id = playerId,
            Name = name,
            Figure = "hr-115",
            Gender = AvatarGenderType.Male,
            PlayerStatus = PlayerStatusType.Offline,
            PlayerPerks = PlayerPerkFlags.None,
            ProfileVisible = visible,
        };

        db.Players.Add(player);

        foreach ((string key, DateTime? publishedAt) in keys)
        {
            db.CameraPhotos.Add(
                new CameraPhotoEntity
                {
                    PhotoKey = key,
                    PlayerEntityId = playerId,
                    SceneBytes = 1024,
                    RejectedExternalSprites = 0,
                    PublishedAt = publishedAt,
                }
            );
        }

        await db.SaveChangesAsync();
    }
}
