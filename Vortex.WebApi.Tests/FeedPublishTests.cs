using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Database.Context;
using Vortex.Database.Entities.Feed;
using Vortex.Database.Entities.Groups;
using Vortex.Database.Entities.Players;
using Vortex.Primitives.Feed;
using Vortex.Primitives.Groups.Enums;
using Vortex.Primitives.Players.Enums;
using Vortex.Primitives.Rooms.Enums;
using Vortex.WebApi.Services;
using Xunit;

namespace Vortex.WebApi.Tests;

/// <summary>
/// What the feed refuses to be told.
/// </summary>
/// <remarks>
/// This is the trust boundary of the whole feature: everything here arrives from a browser and
/// nothing about it is believed. The rule most easily lost in a later edit is the photo ownership
/// check — without it a player attaches somebody else's published photo and it appears under their
/// own name, which looks like a feature until it is used.
/// </remarks>
public sealed class FeedPublishTests
{
    [Fact]
    public async Task AStatusPublishes()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Prodiigy");

        (FeedRefusal refusal, FeedPost? post) = await Service(factory)
            .PublishAsync(
                1,
                new FeedPublishRequest(FeedPostKind.Status, "coucou", null, null, null, null),
                default
            );

        refusal.Should().Be(FeedRefusal.None);
        post!.Body.Should().Be("coucou");
        post.Mine.Should().BeTrue();
    }

    /// <remarks>
    /// Named arguments throughout the three comment facts below. <c>ParentId</c> and
    /// <c>GroupId</c> are adjacent <c>int?</c>, and putting a post id in the wrong one is refused
    /// as <c>GroupNotMember</c> — which surfaces as a null post and a NullReferenceException
    /// wherever the test next touches it, saying nothing whatever about groups.
    /// </remarks>
    [Fact]
    public async Task ACommentAttachesToItsRoot()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Prodiigy");
        await PlayerAsync(factory, 2, "Synes");

        FeedPost root = (
            await Service(factory)
                .PublishAsync(
                    1,
                    new FeedPublishRequest(FeedPostKind.Status, "coucou", null, null, null, null),
                    default
                )
        ).Post!;

        (FeedRefusal refusal, FeedPost? comment) = await Service(factory)
            .PublishAsync(
                2,
                new FeedPublishRequest(
                    Kind: FeedPostKind.Status,
                    Body: "et hop",
                    PhotoKeys: null,
                    PollChoices: null,
                    ParentId: root.Id,
                    GroupId: null
                ),
                default
            );

        refusal.Should().Be(FeedRefusal.None);
        comment!.Comments.Should().BeEmpty("the thread is one level deep, so a comment has none");

        FeedPost reread = (await Service(factory).GetPostAsync(root.Id, null, default))!;

        reread.CommentCount.Should().Be(1);
        reread.Comments.Should().ContainSingle(c => c.Id == comment.Id);

        // Somebody ELSE's comment, and the feed still lists one card: a comment must never surface
        // as a root of its own.
        (await Service(factory).GetFeedAsync(1, 0, null, null, default))
            .Items.Should()
            .ContainSingle(p => p.Id == root.Id);
    }

    [Fact]
    public async Task ACommentOnACommentIsAllowedAndCarriesItsReplies()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Prodiigy");

        FeedPost root = (
            await Service(factory)
                .PublishAsync(
                    1,
                    new FeedPublishRequest(FeedPostKind.Status, "coucou", null, null, null, null),
                    default
                )
        ).Post!;

        FeedPost comment = (
            await Service(factory)
                .PublishAsync(
                    1,
                    new FeedPublishRequest(
                        Kind: FeedPostKind.Status,
                        Body: "et hop",
                        PhotoKeys: null,
                        PollChoices: null,
                        ParentId: root.Id,
                        GroupId: null
                    ),
                    default
                )
        ).Post!;

        (FeedRefusal refusal, FeedPost? reply) = await Service(factory)
            .PublishAsync(
                1,
                new FeedPublishRequest(
                    Kind: FeedPostKind.Status,
                    Body: "encore",
                    PhotoKeys: null,
                    PollChoices: null,
                    ParentId: comment.Id,
                    GroupId: null
                ),
                default
            );

        refusal.Should().Be(FeedRefusal.None);
        reply.Should().NotBeNull();

        // The reply hangs under the COMMENT, not beside it under the root: the root still counts
        // one comment, and that comment carries the reply.
        FeedPost reread = (await Service(factory).GetPostAsync(root.Id, null, default))!;

        reread.CommentCount.Should().Be(1);
        reread.Comments.Should().ContainSingle();
        reread.Comments[0].CommentCount.Should().Be(1);
        reread.Comments[0].Comments.Should().ContainSingle(c => c.Body == "encore");
    }

    [Fact]
    public async Task AReplyToAReplyIsRefused()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Prodiigy");

        FeedPost root = (
            await Service(factory)
                .PublishAsync(
                    1,
                    new FeedPublishRequest(FeedPostKind.Status, "coucou", null, null, null, null),
                    default
                )
        ).Post!;

        FeedPost comment = (await ReplyAsync(factory, root.Id, "et hop"))!;
        FeedPost reply = (await ReplyAsync(factory, comment.Id, "encore"))!;

        // The fourth level is where it stops. That bound is what keeps the read to two batched
        // queries and the page to two indents; without it both become a loop.
        (FeedRefusal refusal, _) = await Service(factory)
            .PublishAsync(
                1,
                new FeedPublishRequest(
                    Kind: FeedPostKind.Status,
                    Body: "et encore",
                    PhotoKeys: null,
                    PollChoices: null,
                    ParentId: reply.Id,
                    GroupId: null
                ),
                default
            );

        refusal.Should().Be(FeedRefusal.ParentNotRoot);
    }

    private static async Task<FeedPost?> ReplyAsync(
        WebApiTestFactory factory,
        int parentId,
        string body
    ) =>
        (
            await Service(factory)
                .PublishAsync(
                    1,
                    new FeedPublishRequest(
                        Kind: FeedPostKind.Status,
                        Body: body,
                        PhotoKeys: null,
                        PollChoices: null,
                        ParentId: parentId,
                        GroupId: null
                    ),
                    default
                )
        ).Post;

    [Fact]
    public async Task ACommentCanCarryAPollOfItsOwn()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Prodiigy");

        FeedPost root = (
            await Service(factory)
                .PublishAsync(
                    1,
                    new FeedPublishRequest(FeedPostKind.Status, "coucou", null, null, null, null),
                    default
                )
        ).Post!;

        // The screenshot gives the comment form the same three tabs as the post form, so a comment
        // has to hold everything a post holds — which is the whole reason the schema makes a
        // comment a post with a parent rather than a second table.
        (FeedRefusal refusal, FeedPost? comment) = await Service(factory)
            .PublishAsync(
                1,
                new FeedPublishRequest(
                    Kind: FeedPostKind.Poll,
                    Body: "et vous ?",
                    PhotoKeys: null,
                    PollChoices: ["Oui", "Non"],
                    ParentId: root.Id,
                    GroupId: null
                ),
                default
            );

        refusal.Should().Be(FeedRefusal.None);
        comment!.Poll!.Choices.Select(c => c.Label).Should().Equal("Oui", "Non");
    }

    [Fact]
    public async Task AnEmptyStatusIsRefused()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Prodiigy");

        (FeedRefusal refusal, _) = await Service(factory)
            .PublishAsync(
                1,
                new FeedPublishRequest(FeedPostKind.Status, "   ", null, null, null, null),
                default
            );

        refusal.Should().Be(FeedRefusal.BodyEmpty);
    }

    [Fact]
    public async Task ABodyOverTheCapIsRefused()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Prodiigy");

        (FeedRefusal refusal, _) = await Service(factory)
            .PublishAsync(
                1,
                new FeedPublishRequest(
                    FeedPostKind.Status,
                    new string('a', 501),
                    null,
                    null,
                    null,
                    null
                ),
                default
            );

        refusal.Should().Be(FeedRefusal.BodyTooLong);
    }

    [Fact]
    public async Task APollWithOneChoiceIsRefused()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Synes");

        (FeedRefusal refusal, _) = await Service(factory)
            .PublishAsync(
                1,
                new FeedPublishRequest(FeedPostKind.Poll, "Pizza ?", null, ["Oui"], null, null),
                default
            );

        refusal.Should().Be(FeedRefusal.PollInvalid);
    }

    [Fact]
    public async Task APollPublishesItsChoicesInOrder()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Synes");

        (FeedRefusal refusal, FeedPost? post) = await Service(factory)
            .PublishAsync(
                1,
                new FeedPublishRequest(
                    FeedPostKind.Poll,
                    "Pizza ou burger ?",
                    null,
                    ["Pizza", "Burger", "Les deux"],
                    null,
                    null
                ),
                default
            );

        refusal.Should().Be(FeedRefusal.None);
        post!.Poll!.Choices.Select(c => c.Label).Should().Equal("Pizza", "Burger", "Les deux");
        post.Poll.TotalVotes.Should().Be(0);
    }

    [Fact]
    public async Task AGroupTheAuthorIsNotInIsRefused()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Prodiigy");

        (FeedRefusal refusal, _) = await Service(factory)
            .PublishAsync(
                1,
                new FeedPublishRequest(
                    FeedPostKind.Status,
                    "au nom du groupe",
                    null,
                    null,
                    null,
                    99
                ),
                default
            );

        refusal.Should().Be(FeedRefusal.GroupNotMember);
    }

    [Fact]
    public async Task ACommentOnADeletedParentIsRefused()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Prodiigy");
        int deletedPostId = await DeletedPostAsync(factory, 2, "Parti");

        (FeedRefusal refusal, _) = await Service(factory)
            .PublishAsync(
                1,
                new FeedPublishRequest(
                    FeedPostKind.Status,
                    "coucou",
                    null,
                    null,
                    deletedPostId,
                    null
                ),
                default
            );

        refusal
            .Should()
            .Be(
                FeedRefusal.ParentNotFound,
                "a deleted post is not there to comment on, and the write path must agree with the read path on that"
            );
    }

    /// <summary>
    /// An author whose profile is hidden still publishes successfully, and gets a non-null post
    /// back. This is the regression the whole round of R20 tests exists for: <c>Readable(db)</c>
    /// used to also require the author's own <c>ProfileVisible</c>, which is <c>false</c> by
    /// default for every new player — so <see cref="WebApiFeedService.PublishAsync"/> returned
    /// <see cref="FeedRefusal.None"/> with a null post the moment it read its own freshly-written
    /// row back through that gate, and the endpoint answered HTTP 200 with a null body.
    /// </summary>
    [Fact]
    public async Task AnAuthorWithAHiddenProfileStillPublishesSuccessfully()
    {
        await using WebApiTestFactory factory = new();
        await HiddenPlayerAsync(factory, 1, "Cache");

        (FeedRefusal refusal, FeedPost? post) = await Service(factory)
            .PublishAsync(
                1,
                new FeedPublishRequest(FeedPostKind.Status, "coucou", null, null, null, null),
                default
            );

        refusal.Should().Be(FeedRefusal.None);
        post.Should()
            .NotBeNull(
                "publishing is the consent — ProfileVisible governs browsing a profile, not "
                    + "posting to the public feed"
            );
        post!.Body.Should().Be("coucou");
    }

    /// <summary>
    /// The other half of the trust boundary the class remark names: a photo attached to a post must
    /// resolve to a row this caller themselves published, never a key that merely exists.
    /// </summary>
    [Fact]
    public async Task APhotoBelongingToSomebodyElseIsRefused()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Prodiigy");
        await PlayerAsync(factory, 2, "Autre");

        await using (VortexDbContext db = await factory.DbContexts.CreateDbContextAsync())
        {
            db.CameraPhotos.Add(
                new Vortex.Database.Entities.Camera.CameraPhotoEntity
                {
                    PhotoKey = "lapienne",
                    PlayerEntityId = 2,
                    SceneBytes = 1,
                    RejectedExternalSprites = 0,
                    PublishedAt = DateTime.UtcNow,
                }
            );

            await db.SaveChangesAsync();
        }

        (FeedRefusal refusal, _) = await Service(factory)
            .PublishAsync(
                1,
                new FeedPublishRequest(FeedPostKind.Photo, "", ["lapienne"], null, null, null),
                default
            );

        refusal.Should().Be(FeedRefusal.PhotoNotYours);
    }

    /// <summary>
    /// Publishing costs credits precisely because it is the act that makes a photo public — an
    /// unpublished photo of the caller's own is still private, so attaching it must refuse exactly
    /// like a stranger's photo would.
    /// </summary>
    [Fact]
    public async Task AnUnpublishedPhotoOfMyOwnIsRefused()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Prodiigy");

        await using (VortexDbContext db = await factory.DbContexts.CreateDbContextAsync())
        {
            db.CameraPhotos.Add(
                new Vortex.Database.Entities.Camera.CameraPhotoEntity
                {
                    PhotoKey = "privee",
                    PlayerEntityId = 1,
                    SceneBytes = 1,
                    RejectedExternalSprites = 0,
                    PublishedAt = null,
                }
            );

            await db.SaveChangesAsync();
        }

        (FeedRefusal refusal, _) = await Service(factory)
            .PublishAsync(
                1,
                new FeedPublishRequest(FeedPostKind.Photo, "", ["privee"], null, null, null),
                default
            );

        refusal.Should().Be(FeedRefusal.PhotoNotYours);
    }

    /// <summary>
    /// The mirror of <see cref="AGroupTheAuthorIsNotInIsRefused"/>: membership is the server's rule,
    /// so a group the author IS in publishes, carries the group's identity on the card, and still
    /// keeps the real author underneath it.
    /// </summary>
    [Fact]
    public async Task AGroupTheAuthorIsInPublishesUnderIt()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Prodiigy");

        int groupId;

        await using (VortexDbContext db = await factory.DbContexts.CreateDbContextAsync())
        {
            // GroupEntity/GroupMemberEntity carry several `required` navigation properties a group
            // otherwise has no use for in this test (the room, the owner's own entity) — GroupFactory
            // is the single creation path that already knows how to leave those null! the way the
            // in-memory provider tolerates (see GroupCreationTests.cs for the same pattern).
            GroupEntity group = GroupFactory.Create(
                name: "Interpol",
                badge: "b0501Xs09114s06017",
                roomEntityId: 42,
                ownerPlayerEntityId: 1,
                type: GroupType.Open,
                colorOne: "#1E5B73",
                colorTwo: "#FFCC00",
                description: "Standard ID"
            );

            db.Groups.Add(group);
            await db.SaveChangesAsync();

            db.GroupMembers.Add(
                new GroupMemberEntity
                {
                    GroupEntityId = group.Id,
                    PlayerEntityId = 1,
                    Rank = GroupMemberRank.Member,
                    GroupEntity = group,
                    PlayerEntity = null!,
                }
            );

            await db.SaveChangesAsync();

            groupId = group.Id;
        }

        (FeedRefusal refusal, FeedPost? post) = await Service(factory)
            .PublishAsync(
                1,
                new FeedPublishRequest(
                    FeedPostKind.Status,
                    "au nom du groupe",
                    null,
                    null,
                    null,
                    groupId
                ),
                default
            );

        refusal.Should().Be(FeedRefusal.None);
        post!.GroupId.Should().Be(groupId);
        post.GroupName.Should().Be("Interpol");
        post.AuthorName.Should()
            .Be("Prodiigy", "the row keeps the person even when the card shows the group");
    }

    private static IWebApiFeedService Service(WebApiTestFactory factory) =>
        new WebApiFeedService(factory.DbContexts, factory.Events);

    private static async Task PlayerAsync(WebApiTestFactory factory, int id, string name)
    {
        await using VortexDbContext db = await factory.DbContexts.CreateDbContextAsync();

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

        await db.SaveChangesAsync();
    }

    /// <summary>A player whose profile is hidden — <c>ProfileVisible</c> is a new player's default.</summary>
    private static async Task HiddenPlayerAsync(WebApiTestFactory factory, int id, string name)
    {
        await using VortexDbContext db = await factory.DbContexts.CreateDbContextAsync();

        db.Players.Add(
            new PlayerEntity
            {
                Id = id,
                Name = name,
                Figure = "hr-115",
                Gender = AvatarGenderType.Male,
                PlayerStatus = PlayerStatusType.Offline,
                PlayerPerks = PlayerPerkFlags.None,
                ProfileVisible = false,
            }
        );

        await db.SaveChangesAsync();
    }

    /// <summary>A root post, already soft-deleted. Returns the post's id.</summary>
    private static async Task<int> DeletedPostAsync(
        WebApiTestFactory factory,
        int playerId,
        string name
    )
    {
        await PlayerAsync(factory, playerId, name);

        await using VortexDbContext db = await factory.DbContexts.CreateDbContextAsync();

        WebFeedPostEntity post = new()
        {
            PlayerEntityId = playerId,
            Kind = FeedPostKind.Status,
            Body = "parti",
            DeletedAt = DateTime.UtcNow,
        };
        db.WebFeedPosts.Add(post);

        await db.SaveChangesAsync();

        return post.Id;
    }
}
