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
/// A comment is a post with a parent, so the feed has to return roots ONLY or every thread appears
/// twice — that is the one rule not obvious from the route. A post by a player who has hidden their
/// profile is NOT excluded: publishing to the public feed is a different act from letting someone
/// browse the profile, and <c>ProfileVisible</c> only governs the second (see
/// <see cref="WebApiFeedService.Readable"/> for why). A photo attached to a post is still re-checked
/// for published/not-deleted, because the owner can take it back after posting; only the
/// <c>ProfileVisible</c> half of that re-check is gone, for the same publishing-is-consent reason.
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
    public async Task Feed_KeepsAPostByAHiddenPlayerBecausePublishingIsTheConsent()
    {
        await using WebApiTestFactory factory = new();
        await SeedPostAsync(factory, 1, "Cache", visible: false, body: "toujours visible");

        (await Service(factory).GetFeedAsync(1, 0, null, null, default))
            .Items.Should()
            .ContainSingle(
                p => p.Body == "toujours visible",
                "ProfileVisible governs browsing a profile, not publishing to the public feed — a "
                    + "player who posts has already consented to that post being read"
            );
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

    /// <summary>
    /// <see cref="WebApiFeedService"/> answers a page by fetching each supporting table ONCE for
    /// every post on the page, rather than once per post — see the class's own remarks. Batching by
    /// id set is exactly how one post could end up carrying another's photos, poll or counters if the
    /// grouping were wrong; a single-post fixture cannot catch that, because there is nothing to leak
    /// from. Two roots by two different players, each with its own distinct photo, poll, tag,
    /// reaction and comment count, read in the SAME <see cref="IWebApiFeedService.GetFeedAsync"/>
    /// call, is the smallest fixture that can.
    /// </summary>
    [Fact]
    public async Task Feed_KeepsEachPostsOwnDataSeparateWhenBatched()
    {
        await using WebApiTestFactory factory = new();

        int photoPostId = await SeedPhotoPostAsync(
            factory,
            playerId: 1,
            name: "Alpha",
            key: "alpha-key",
            published: true
        );
        await SeedTagAsync(factory, photoPostId, "alpha");
        await SeedCommentAsync(factory, playerId: 1, parentId: photoPostId, body: "a1");

        (int pollPostId, int choiceId) = await SeedPollAsync(factory, playerId: 2, name: "Beta");
        await SeedTagAsync(factory, pollPostId, "beta");
        await SeedCommentAsync(factory, playerId: 2, parentId: pollPostId, body: "b1");
        await SeedCommentAsync(factory, playerId: 2, parentId: pollPostId, body: "b2");

        await using (VortexDbContext db = await factory.DbContexts.CreateDbContextAsync())
        {
            db.WebFeedReactions.Add(
                new WebFeedReactionEntity
                {
                    WebFeedPostEntityId = photoPostId,
                    PlayerEntityId = 2,
                    Kind = FeedReactionKind.Favorite,
                }
            );
            db.WebFeedPollVotes.Add(
                new WebFeedPollVoteEntity
                {
                    WebFeedPostEntityId = pollPostId,
                    WebFeedPollChoiceEntityId = choiceId,
                    PlayerEntityId = 1,
                }
            );
            await db.SaveChangesAsync();
        }

        FeedPage page = await Service(factory).GetFeedAsync(1, 0, null, null, default);

        page.Items.Should().HaveCount(2);

        FeedPost photoPost = page.Items.Single(p => p.Id == photoPostId);
        FeedPost pollPost = page.Items.Single(p => p.Id == pollPostId);

        photoPost.Photos.Should().ContainSingle(p => p.Key == "alpha-key");
        photoPost.Poll.Should().BeNull();
        photoPost.Tags.Should().Equal("alpha");
        photoPost.CommentCount.Should().Be(1);
        photoPost.FavoriteCount.Should().Be(1);
        photoPost.RepostCount.Should().Be(0);

        pollPost.Photos.Should().BeEmpty();
        pollPost.Poll.Should().NotBeNull();
        pollPost.Poll!.Choices.Single(c => c.Id == choiceId).Votes.Should().Be(1);
        pollPost.Poll.TotalVotes.Should().Be(1);
        pollPost.Tags.Should().Equal("beta");
        pollPost.CommentCount.Should().Be(2);
        pollPost.FavoriteCount.Should().Be(0);
    }

    /// <summary>
    /// A share puts the post in the feed AGAIN, under the sharer's name — which is what makes it
    /// a repost rather than a counter.
    /// </summary>
    /// <remarks>
    /// It was a counter for a while: pressing share wrote a reaction row, moved a number, and put
    /// the post in nobody's feed. The button said "share" and shared nothing.
    /// </remarks>
    [Fact]
    public async Task ASharePutsThePostInTheFeedAgainUnderTheSharersName()
    {
        await using WebApiTestFactory factory = new();

        int postId = await SeedPostAsync(factory, 1, "Prodiigy", visible: true, "mon message");
        await EnsurePlayerAsync(factory, 2, "Chloe", visible: true);

        await Service(factory).SetReactionAsync(2, postId, FeedReactionKind.Repost, true, default);

        FeedPage page = await Service(factory).GetFeedAsync(1, 0, null, null, default);

        // Two entries for ONE post: the original and the share.
        page.Total.Should().Be(2);
        page.Items.Should().HaveCount(2);
        page.Items.Should().OnlyContain(p => p.Id == postId);

        // The share is newer, so it comes first, and it is the one carrying the sharer.
        page.Items[0].Reposter.Should().NotBeNull();
        page.Items[0].Reposter!.Name.Should().Be("Chloe");
        page.Items[0].Reposter!.FigureString.Should().Be("hr-115");

        // The original is still the original: nobody put it there.
        page.Items[1].Reposter.Should().BeNull();

        // And the body is the AUTHOR'S on both — a share points at the post, it does not copy it.
        page.Items.Should()
            .OnlyContain(p => p.Body == "mon message" && p.AuthorName == "Prodiigy");
    }

    /// <summary>Withdrawing the share takes its entry out again.</summary>
    [Fact]
    public async Task UnsharingRemovesTheEntry()
    {
        await using WebApiTestFactory factory = new();

        int postId = await SeedPostAsync(factory, 1, "Prodiigy", visible: true, "mon message");
        await EnsurePlayerAsync(factory, 2, "Chloe", visible: true);

        await Service(factory).SetReactionAsync(2, postId, FeedReactionKind.Repost, true, default);
        await Service(factory).SetReactionAsync(2, postId, FeedReactionKind.Repost, false, default);

        FeedPage page = await Service(factory).GetFeedAsync(1, 0, null, null, default);

        page.Total.Should().Be(1);
        page.Items.Should().ContainSingle().Which.Reposter.Should().BeNull();
    }

    /// <summary>
    /// A LIKE is not a share and puts nothing in the feed. The two reactions live in one table and
    /// differ by a single column, so this is the assertion that keeps them apart.
    /// </summary>
    [Fact]
    public async Task ALikeDoesNotPutThePostInTheFeedAgain()
    {
        await using WebApiTestFactory factory = new();

        int postId = await SeedPostAsync(factory, 1, "Prodiigy", visible: true, "mon message");
        await EnsurePlayerAsync(factory, 2, "Chloe", visible: true);

        await Service(factory)
            .SetReactionAsync(2, postId, FeedReactionKind.Favorite, true, default);

        FeedPage page = await Service(factory).GetFeedAsync(1, 0, null, null, default);

        page.Total.Should().Be(1);
        page.Items.Should().ContainSingle().Which.FavoriteCount.Should().Be(1);
    }

    /// <summary>
    /// A post read by id is the original and never carries a sharer — the sharer belongs to the
    /// feed ENTRY, and a direct read is not one.
    /// </summary>
    [Fact]
    public async Task ReadingAPostByIdNeverCarriesASharer()
    {
        await using WebApiTestFactory factory = new();

        int postId = await SeedPostAsync(factory, 1, "Prodiigy", visible: true, "mon message");
        await EnsurePlayerAsync(factory, 2, "Chloe", visible: true);

        await Service(factory).SetReactionAsync(2, postId, FeedReactionKind.Repost, true, default);

        (await Service(factory).GetPostAsync(postId, null, default))!.Reposter.Should().BeNull();
    }

    private static IWebApiFeedService Service(WebApiTestFactory factory) =>
        new WebApiFeedService(factory.DbContexts, factory.Events, factory.WordFilter);

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

    private static async Task SeedTagAsync(WebApiTestFactory factory, int postId, string tag)
    {
        await using VortexDbContext db = await factory.DbContexts.CreateDbContextAsync();

        WebFeedTagEntity tagEntity = new() { Tag = tag };
        db.WebFeedTags.Add(tagEntity);
        await db.SaveChangesAsync();

        db.WebFeedPostTags.Add(
            new WebFeedPostTagEntity
            {
                WebFeedPostEntityId = postId,
                WebFeedTagEntityId = tagEntity.Id,
            }
        );
        await db.SaveChangesAsync();
    }
}
