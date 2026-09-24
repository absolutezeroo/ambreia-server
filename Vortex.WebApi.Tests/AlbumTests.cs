using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Database.Context;
using Vortex.Database.Entities.Camera;
using Vortex.Database.Entities.Players;
using Vortex.Primitives.Feed;
using Vortex.Primitives.Moderation.WordFilter;
using Vortex.Primitives.Players.Enums;
using Vortex.Primitives.Rooms.Enums;
using Vortex.WebApi.Services;
using Xunit;

namespace Vortex.WebApi.Tests;

/// <summary>
/// Photo albums: the owner's own published photos only, a cap, the word filter, a card in the feed
/// that comes back up when photos are added, and an album that goes when its card goes.
/// </summary>
public sealed class AlbumTests
{
    [Fact]
    public async Task CreatingAnAlbum_PostsItsCard()
    {
        await using WebApiTestFactory factory = new();
        await SeedAsync(factory);

        Album album = await CreateAsync(factory, "a", "b");

        album.Title.Should().Be("Vacances");
        album.Photos.Select(p => p.Key).Should().Equal("a", "b");

        FeedPost card = (
            await Feed(factory).GetFeedAsync(1, 0, null, null, default)
        ).Items.Single();
        card.Id.Should().Be(album.FeedPostId);
        card.Kind.Should().Be(FeedPostKind.Album);
        card.Album!.Id.Should().Be(album.Id);
        card.Album.PhotoCount.Should().Be(2);
    }

    [Fact]
    public async Task SomebodyElsesPhoto_IsRefused()
    {
        await using WebApiTestFactory factory = new();
        await SeedAsync(factory);

        (AlbumRefusal refusal, _) = await Albums(factory)
            .CreateAsync(1, new AlbumCreateRequest("Vacances", null, ["autre"]), default);

        refusal.Should().Be(AlbumRefusal.PhotoNotYours);
    }

    [Fact]
    public async Task AnUnpublishedPhoto_IsRefused()
    {
        await using WebApiTestFactory factory = new();
        await SeedAsync(factory);

        (AlbumRefusal refusal, _) = await Albums(factory)
            .CreateAsync(1, new AlbumCreateRequest("Vacances", null, ["privee"]), default);

        refusal.Should().Be(AlbumRefusal.PhotoNotYours);
    }

    [Fact]
    public async Task AnAlbumWithNoPhotos_IsRefused()
    {
        await using WebApiTestFactory factory = new();
        await SeedAsync(factory);

        (AlbumRefusal refusal, _) = await Albums(factory)
            .CreateAsync(1, new AlbumCreateRequest("Vacances", null, []), default);

        refusal.Should().Be(AlbumRefusal.Empty);
    }

    [Fact]
    public async Task ARefusedTitle_WritesNothing()
    {
        await using WebApiTestFactory factory = new();
        await SeedAsync(factory);
        factory.WordFilter.Answer = text => new WordFilterResult(text, text == "con", false);

        (AlbumRefusal refusal, _) = await Albums(factory)
            .CreateAsync(1, new AlbumCreateRequest("con", null, ["a"]), default);

        refusal.Should().Be(AlbumRefusal.WordFiltered);
        factory.WordFilter.Applied.Should().Contain(("con", WordFilterSource.Album, 1));
        (await Feed(factory).GetFeedAsync(1, 0, null, null, default)).Items.Should().BeEmpty();
    }

    [Fact]
    public async Task AddingPhotos_BringsTheCardBackUp()
    {
        await using WebApiTestFactory factory = new();
        await SeedAsync(factory);

        Album album = await CreateAsync(factory, "a");

        await Feed(factory)
            .PublishAsync(
                1,
                new FeedPublishRequest(FeedPostKind.Status, "plus récent", null, null, null, null),
                default
            );

        (await Feed(factory).GetFeedAsync(1, 0, null, null, default))
            .Items[0]
            .Kind.Should()
            .Be(FeedPostKind.Status);

        (AlbumRefusal refusal, Album? grown) = await Albums(factory)
            .AddPhotosAsync(1, album.Id, new AlbumPhotosRequest(["a", "b"]), default);

        refusal.Should().Be(AlbumRefusal.None);
        // "a" was already in it: skipped, not doubled.
        grown!.Photos.Select(p => p.Key).Should().Equal("a", "b");

        FeedPage feed = await Feed(factory).GetFeedAsync(1, 0, null, null, default);
        feed.Items[0].Id.Should().Be(album.FeedPostId);
        feed.Items[0].Album!.PhotoCount.Should().Be(2);
    }

    [Fact]
    public async Task AFullAlbum_RefusesMore()
    {
        await using WebApiTestFactory factory = new();
        await SeedAsync(factory);

        string[] keys =
        [
            .. Enumerable.Range(0, WebApiAlbumService.MaximumPhotos).Select(i => $"n{i}"),
        ];
        await PhotosAsync(factory, 1, keys);

        (AlbumRefusal created, Album? album) = await Albums(factory)
            .CreateAsync(1, new AlbumCreateRequest("Plein", null, keys), default);

        created.Should().Be(AlbumRefusal.None);

        (AlbumRefusal refusal, _) = await Albums(factory)
            .AddPhotosAsync(1, album!.Id, new AlbumPhotosRequest(["a"]), default);

        refusal.Should().Be(AlbumRefusal.Full);
    }

    [Fact]
    public async Task SomebodyElsesAlbum_CannotBeChanged()
    {
        await using WebApiTestFactory factory = new();
        await SeedAsync(factory);

        Album album = await CreateAsync(factory, "a");

        (AlbumRefusal refusal, _) = await Albums(factory)
            .EditAsync(2, album.Id, new AlbumEditRequest("À moi", null), default);

        refusal.Should().Be(AlbumRefusal.NotFound);
    }

    [Fact]
    public async Task RemovingAPhoto_LetsItComeBack()
    {
        await using WebApiTestFactory factory = new();
        await SeedAsync(factory);

        Album album = await CreateAsync(factory, "a", "b");

        (_, Album? smaller) = await Albums(factory).RemovePhotoAsync(1, album.Id, "a", default);
        smaller!.Photos.Select(p => p.Key).Should().Equal("b");

        (_, Album? back) = await Albums(factory)
            .AddPhotosAsync(1, album.Id, new AlbumPhotosRequest(["a"]), default);
        back!.Photos.Select(p => p.Key).Should().Equal("b", "a");
    }

    [Fact]
    public async Task DeletingTheCard_DeletesTheAlbum()
    {
        await using WebApiTestFactory factory = new();
        await SeedAsync(factory);

        Album album = await CreateAsync(factory, "a");

        (await Feed(factory).DeleteAsync(1, album.FeedPostId, default)).Should().BeTrue();

        (await Albums(factory).GetAsync(album.Id, null, default)).Should().BeNull();
        (await Albums(factory).ListAsync(1, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task TheFeedsComposer_CannotPostAnAlbumCard()
    {
        await using WebApiTestFactory factory = new();
        await SeedAsync(factory);

        (FeedRefusal refusal, _) = await Feed(factory)
            .PublishAsync(
                1,
                new FeedPublishRequest(FeedPostKind.Album, "x", null, null, null, null),
                default
            );

        refusal.Should().Be(FeedRefusal.KindMismatch);
    }

    private static IWebApiAlbumService Albums(WebApiTestFactory factory) =>
        new WebApiAlbumService(factory.DbContexts, factory.Events, factory.WordFilter);

    private static IWebApiFeedService Feed(WebApiTestFactory factory) =>
        new WebApiFeedService(factory.DbContexts, factory.Events, factory.WordFilter);

    private static async Task<Album> CreateAsync(WebApiTestFactory factory, params string[] keys)
    {
        (AlbumRefusal refusal, Album? album) = await Albums(factory)
            .CreateAsync(1, new AlbumCreateRequest("Vacances", "Été", keys), default);

        refusal.Should().Be(AlbumRefusal.None);

        return album!;
    }

    /// <summary>
    /// Player 1 owns published "a" and "b" and unpublished "privee"; player 2 owns "autre".
    /// </summary>
    private static async Task SeedAsync(WebApiTestFactory factory)
    {
        await using (VortexDbContext db = await factory.DbContexts.CreateDbContextAsync())
        {
            foreach ((int id, string name) in new[] { (1, "Prodiigy"), (2, "Autre") })
            {
                db.Players.Add(
                    new PlayerEntity
                    {
                        Id = id,
                        Name = name,
                        Figure = "hr-115",
                        Gender = AvatarGenderType.Male,
                        PlayerStatus = PlayerStatusType.Offline,
                        PlayerPerks = PlayerPerkFlags.None,
                        ProfileVisible = true,
                    }
                );
            }

            await db.SaveChangesAsync();
        }

        await PhotosAsync(factory, 1, ["a", "b"]);
        await PhotosAsync(factory, 2, ["autre"]);
        await PhotosAsync(factory, 1, ["privee"], published: false);
    }

    private static async Task PhotosAsync(
        WebApiTestFactory factory,
        int ownerId,
        string[] keys,
        bool published = true
    )
    {
        await using VortexDbContext db = await factory.DbContexts.CreateDbContextAsync();

        foreach (string key in keys)
        {
            db.CameraPhotos.Add(
                new CameraPhotoEntity
                {
                    PhotoKey = key,
                    PlayerEntityId = ownerId,
                    SceneBytes = 1,
                    RejectedExternalSprites = 0,
                    PublishedAt = published ? DateTime.UtcNow : null,
                }
            );
        }

        await db.SaveChangesAsync();
    }
}
