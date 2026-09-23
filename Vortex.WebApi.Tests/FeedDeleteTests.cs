using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Context;
using Vortex.Database.Entities.Players;
using Vortex.Primitives.Feed;
using Vortex.Primitives.Players.Enums;
using Vortex.Primitives.Rooms.Enums;
using Vortex.WebApi.Services;
using Xunit;

namespace Vortex.WebApi.Tests;

/// <summary>
/// Taking a post back.
/// </summary>
/// <remarks>
/// A soft delete, so the row survives for the audit trail the rest of this hotel keeps — and
/// because <c>Readable(db)</c> is the single gate, one <c>DeletedAt</c> is what removes a post
/// from the feed, from a direct read, from voting and from reacting all at once.
/// </remarks>
public sealed class FeedDeleteTests
{
    [Fact]
    public async Task IDeleteMyOwn()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Prodiigy");
        FeedPost post = await StatusAsync(factory, 1);

        (await Service(factory).DeleteAsync(1, post.Id, default)).Should().BeTrue();
        (await Service(factory).GetPostAsync(post.Id, null, default)).Should().BeNull();
        (await Service(factory).GetFeedAsync(1, 0, null, null, default)).Items.Should().BeEmpty();
    }

    [Fact]
    public async Task IDoNotDeleteSomebodyElses()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Prodiigy");
        await PlayerAsync(factory, 2, "Autre");
        FeedPost post = await StatusAsync(factory, 1);

        (await Service(factory).DeleteAsync(2, post.Id, default)).Should().BeFalse();
        (await Service(factory).GetPostAsync(post.Id, null, default)).Should().NotBeNull();
    }

    [Fact]
    public async Task DeletingTwiceIsRefusedRatherThanCounted()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Prodiigy");
        FeedPost post = await StatusAsync(factory, 1);

        (await Service(factory).DeleteAsync(1, post.Id, default)).Should().BeTrue();

        // The second call finds nothing live to delete. False, not an exception and not a silent
        // true — a page that retried should learn the post is already gone.
        (await Service(factory).DeleteAsync(1, post.Id, default))
            .Should()
            .BeFalse();
    }

    [Fact]
    public async Task DeletingARootTakesItsCommentsWithIt()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Prodiigy");
        await PlayerAsync(factory, 2, "Autre");

        FeedPost root = await StatusAsync(factory, 1);

        FeedPost child = (
            await Service(factory)
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
                )
        ).Post!;

        // And a REPLY under that comment — the second level the thread allows. Deleting the root
        // has to reach it too, or it survives under a parent nobody can reach.
        FeedPost grandChild = (
            await Service(factory)
                .PublishAsync(
                    1,
                    new FeedPublishRequest(
                        Kind: FeedPostKind.Status,
                        Body: "encore",
                        PhotoKeys: null,
                        PollChoices: null,
                        ParentId: child.Id,
                        GroupId: null
                    ),
                    default
                )
        ).Post!;

        await Service(factory).DeleteAsync(1, root.Id, default);

        // Somebody ELSE's comment goes with the post it was made on: it is unreadable out of the
        // thread it answers, and leaving it live would put it in nobody's feed and nobody's reach.
        (await Service(factory).GetPostAsync(child.Id, null, default))
            .Should()
            .BeNull();
        (await Service(factory).GetPostAsync(grandChild.Id, null, default)).Should().BeNull();
    }

    [Fact]
    public async Task ADeletedPostCannotBeCommentedOn()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Prodiigy");
        FeedPost post = await StatusAsync(factory, 1);

        await Service(factory).DeleteAsync(1, post.Id, default);

        (FeedRefusal refusal, _) = await Service(factory)
            .PublishAsync(
                1,
                new FeedPublishRequest(
                    Kind: FeedPostKind.Status,
                    Body: "trop tard",
                    PhotoKeys: null,
                    PollChoices: null,
                    ParentId: post.Id,
                    GroupId: null
                ),
                default
            );

        refusal.Should().Be(FeedRefusal.ParentNotFound);
    }

    [Fact]
    public async Task TheRowSurvivesTheDelete()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Prodiigy");
        FeedPost post = await StatusAsync(factory, 1);

        await Service(factory).DeleteAsync(1, post.Id, default);

        await using VortexDbContext db = await factory.DbContexts.CreateDbContextAsync();

        // `IgnoreQueryFilters` because every VortexEntity carries a GLOBAL soft-delete filter
        // (`ModelBuilderExtensions.ApplySoftDeleteQueryFilter`) — an ordinary query cannot see the
        // row at all, which is the point of the filter and is also why this assertion needs the
        // opt-out. Soft, not hard: the audit trail keeps it.
        (await db.WebFeedPosts.IgnoreQueryFilters().CountAsync())
            .Should()
            .Be(1);
        (await db.WebFeedPosts.IgnoreQueryFilters().SingleAsync()).DeletedAt.Should().NotBeNull();
    }

    private static IWebApiFeedService Service(WebApiTestFactory factory) =>
        new WebApiFeedService(factory.DbContexts, factory.Events, factory.WordFilter);

    private static async Task<FeedPost> StatusAsync(WebApiTestFactory factory, int playerId) =>
        (
            await Service(factory)
                .PublishAsync(
                    playerId,
                    new FeedPublishRequest(FeedPostKind.Status, "coucou", null, null, null, null),
                    default
                )
        ).Post!;

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
}
