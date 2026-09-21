using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Database.Context;
using Vortex.Database.Entities.Camera;
using Vortex.Database.Entities.Feed;
using Vortex.Database.Entities.Players;
using Vortex.Primitives.Feed;
using Vortex.Primitives.Players.Enums;
using Vortex.Primitives.Rooms.Enums;
using Vortex.WebApi.Services;
using Xunit;

namespace Vortex.WebApi.Tests;

/// <summary>
/// What leaves the feed's read side.
/// </summary>
/// <remarks>
/// Two rules carry every test here and neither is obvious from the route. A comment is a post with
/// a parent, so the feed has to return roots ONLY or every thread appears twice; and a post by a
/// player who has hidden their profile must not appear at all, which is the same disclosure rule
/// the photo gallery enforces and the one most likely to be dropped in a later edit, because it
/// lives on a different table from the post.
/// </remarks>
public sealed class FeedReadTests
{
    [Fact]
    public async Task Feed_ReturnsRootsOnly()
    {
        await using WebApiTestFactory factory = new();
        int root = await SeedPostAsync(
            factory,
            playerId: 1,
            name: "Prodiigy",
            visible: true,
            body: "coucou"
        );
        await SeedCommentAsync(factory, playerId: 1, parentId: root, body: "et hop");

        FeedPage page = await Service(factory).GetFeedAsync(1, 0, null, null, default);

        page.Items.Should().ContainSingle();
        page.Items[0].Id.Should().Be(root);
        page.Items[0].CommentCount.Should().Be(1);
        page.Total.Should().Be(1);
    }

    [Fact]
    public async Task Feed_CarriesTheThreeMostRecentComments()
    {
        await using WebApiTestFactory factory = new();
        int root = await SeedPostAsync(factory, 1, "Prodiigy", true, "coucou");

        for (int i = 0; i < 5; i++)
        {
            await SeedCommentAsync(factory, 1, root, $"c{i}");
        }

        FeedPost post = (await Service(factory).GetFeedAsync(1, 0, null, null, default)).Items[0];

        post.CommentCount.Should().Be(5);
        post.Comments.Should().HaveCount(3);
        post.Comments.Select(c => c.Body).Should().Equal("c2", "c3", "c4");
    }

    [Fact]
    public async Task Feed_LeavesOutAPostByAHiddenPlayer()
    {
        await using WebApiTestFactory factory = new();
        await SeedPostAsync(factory, 1, "Cache", visible: false, body: "invisible");

        (await Service(factory).GetFeedAsync(1, 0, null, null, default)).Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Feed_LeavesOutADeletedPost()
    {
        await using WebApiTestFactory factory = new();
        int id = await SeedPostAsync(factory, 1, "Prodiigy", true, "parti");

        await using (VortexDbContext db = await factory.DbContexts.CreateDbContextAsync())
        {
            (await db.WebFeedPosts.FindAsync(id))!.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        (await Service(factory).GetFeedAsync(1, 0, null, null, default)).Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Post_DropsAPhotoTheOwnerUnpublished()
    {
        await using WebApiTestFactory factory = new();
        int id = await SeedPhotoPostAsync(
            factory,
            playerId: 1,
            name: "Prodiigy",
            key: "abc",
            published: false
        );

        FeedPost? post = await Service(factory).GetPostAsync(id, null, default);

        post.Should().NotBeNull();
        post!
            .Photos.Should()
            .BeEmpty("a photo taken back must leave the post, not stay in it as a hole");
    }

    [Fact]
    public async Task Post_ReportsMyOwnVote()
    {
        await using WebApiTestFactory factory = new();
        (int postId, int choiceId) = await SeedPollAsync(factory, playerId: 1, name: "Synes");

        await using (VortexDbContext db = await factory.DbContexts.CreateDbContextAsync())
        {
            db.WebFeedPollVotes.Add(
                new WebFeedPollVoteEntity
                {
                    WebFeedPostEntityId = postId,
                    WebFeedPollChoiceEntityId = choiceId,
                    PlayerEntityId = 1,
                }
            );
            await db.SaveChangesAsync();
        }

        FeedPost post = (await Service(factory).GetPostAsync(postId, viewerId: 1, default))!;

        post.Poll!.TotalVotes.Should().Be(1);
        post.Poll.MyChoiceId.Should().Be(choiceId);
        post.Poll.Choices.Single(c => c.Id == choiceId).Votes.Should().Be(1);
    }

    private static IWebApiFeedService Service(WebApiTestFactory factory) =>
        new WebApiFeedService(factory.DbContexts);

    private static async Task<PlayerEntity> EnsurePlayerAsync(
        WebApiTestFactory factory,
        int playerId,
        string name,
        bool visible
    )
    {
        await using VortexDbContext db = await factory.DbContexts.CreateDbContextAsync();

        PlayerEntity? existing = await db.Players.FindAsync(playerId);

        if (existing is not null)
        {
            return existing;
        }

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
        await db.SaveChangesAsync();

        return player;
    }

    private static async Task<int> SeedPostAsync(
        WebApiTestFactory factory,
        int playerId,
        string name,
        bool visible,
        string body
    )
    {
        await EnsurePlayerAsync(factory, playerId, name, visible);

        await using VortexDbContext db = await factory.DbContexts.CreateDbContextAsync();

        WebFeedPostEntity post = new()
        {
            PlayerEntityId = playerId,
            Kind = FeedPostKind.Status,
            Body = body,
        };
        db.WebFeedPosts.Add(post);
        await db.SaveChangesAsync();

        return post.Id;
    }

    private static async Task SeedCommentAsync(
        WebApiTestFactory factory,
        int playerId,
        int parentId,
        string body
    )
    {
        await using VortexDbContext db = await factory.DbContexts.CreateDbContextAsync();

        db.WebFeedPosts.Add(
            new WebFeedPostEntity
            {
                PlayerEntityId = playerId,
                Kind = FeedPostKind.Status,
                Body = body,
                ParentEntityId = parentId,
            }
        );

        await db.SaveChangesAsync();
    }

    private static async Task<int> SeedPhotoPostAsync(
        WebApiTestFactory factory,
        int playerId,
        string name,
        string key,
        bool published
    )
    {
        await EnsurePlayerAsync(factory, playerId, name, visible: true);

        await using VortexDbContext db = await factory.DbContexts.CreateDbContextAsync();

        CameraPhotoEntity photo = new()
        {
            PhotoKey = key,
            PlayerEntityId = playerId,
            SceneBytes = 1024,
            RejectedExternalSprites = 0,
            PublishedAt = published ? DateTime.UtcNow : null,
        };

        WebFeedPostEntity post = new()
        {
            PlayerEntityId = playerId,
            Kind = FeedPostKind.Photo,
            Body = "",
        };

        db.CameraPhotos.Add(photo);
        db.WebFeedPosts.Add(post);
        await db.SaveChangesAsync();

        db.WebFeedPostPhotos.Add(
            new WebFeedPostPhotoEntity
            {
                WebFeedPostEntityId = post.Id,
                CameraPhotoEntityId = photo.Id,
                Ordinal = 0,
            }
        );

        await db.SaveChangesAsync();

        return post.Id;
    }

    private static async Task<(int PostId, int ChoiceId)> SeedPollAsync(
        WebApiTestFactory factory,
        int playerId,
        string name
    )
    {
        await EnsurePlayerAsync(factory, playerId, name, visible: true);

        await using VortexDbContext db = await factory.DbContexts.CreateDbContextAsync();

        WebFeedPostEntity post = new()
        {
            PlayerEntityId = playerId,
            Kind = FeedPostKind.Poll,
            Body = "Pizza ou burger ?",
        };
        db.WebFeedPosts.Add(post);
        await db.SaveChangesAsync();

        WebFeedPollChoiceEntity choice = new()
        {
            WebFeedPostEntityId = post.Id,
            Ordinal = 0,
            Label = "Pizza",
        };
        db.WebFeedPollChoices.Add(choice);
        db.WebFeedPollChoices.Add(
            new WebFeedPollChoiceEntity
            {
                WebFeedPostEntityId = post.Id,
                Ordinal = 1,
                Label = "Burger",
            }
        );
        await db.SaveChangesAsync();

        return (post.Id, choice.Id);
    }
}
