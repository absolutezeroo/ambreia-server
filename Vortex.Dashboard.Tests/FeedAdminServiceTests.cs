using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Vortex.Dashboard.API.Admin.Hotel;
using Vortex.Database.Context;
using Vortex.Database.Entities.Feed;
using Vortex.Primitives.Feed;
using Xunit;

namespace Vortex.Dashboard.Tests;

/// <summary>
/// The operator's takedown on the website's social feed.
/// </summary>
/// <remarks>
/// <para>
/// The difference from the player's own delete — <c>WebApiFeedService.DeleteAsync</c> — is exactly
/// one condition: this does not ask whose post it is. That is the whole feature, and
/// <see cref="RemovesAPostWhoeverWroteIt"/> is the test that would fail if an ownership check ever
/// crept in from the other implementation.
/// </para>
/// <para>
/// There is no capability check in the service and none is tested here: the route carries
/// <c>Capabilities.Dashboard.OpsFeedModerate</c>, and a second check in a second place would be a
/// second thing to get wrong. What the capability's own declaration guards is
/// <c>CapabilityDeclarationTests</c>, which fails if it is not in <c>Capabilities.Dashboard.All</c>
/// — the list that registers the authorization policies.
/// </para>
/// </remarks>
public sealed class FeedAdminServiceTests
{
    [Fact]
    public async Task RemovesAPostWhoeverWroteIt()
    {
        DbContextOptions<VortexDbContext> options = NewOptions();
        int postId = await SeedPostAsync(options, playerId: 7);

        (await new FeedAdminService(new TestDbContextFactory(options)).RemoveAsync(postId, default))
            .Should()
            .BeTrue();

        await using VortexDbContext db = new(options);

        // IgnoreQueryFilters because every VortexEntity carries a global soft-delete filter — the
        // row is still there, an ordinary query simply cannot see it, which is the point.
        (await db.WebFeedPosts.IgnoreQueryFilters().SingleAsync(p => p.Id == postId))
            .DeletedAt.Should()
            .NotBeNull();
    }

    [Fact]
    public async Task TakesTheCommentsWithIt()
    {
        DbContextOptions<VortexDbContext> options = NewOptions();
        int postId = await SeedPostAsync(options, playerId: 7);
        int commentId;

        await using (VortexDbContext seed = new(options))
        {
            WebFeedPostEntity comment = new()
            {
                // Somebody else's comment: it goes too. A comment is unreadable out of the thread
                // it answers, and the feed lists roots only.
                PlayerEntityId = 9,
                Kind = FeedPostKind.Status,
                Body = "et hop",
                ParentEntityId = postId,
            };

            seed.WebFeedPosts.Add(comment);
            await seed.SaveChangesAsync();

            commentId = comment.Id;
        }

        await new FeedAdminService(new TestDbContextFactory(options)).RemoveAsync(postId, default);

        await using VortexDbContext db = new(options);

        (await db.WebFeedPosts.IgnoreQueryFilters().SingleAsync(p => p.Id == commentId))
            .DeletedAt.Should()
            .NotBeNull();
    }

    [Fact]
    public async Task RemovingTwiceAnswersFalse()
    {
        DbContextOptions<VortexDbContext> options = NewOptions();
        int postId = await SeedPostAsync(options, playerId: 7);
        FeedAdminService service = new(new TestDbContextFactory(options));

        (await service.RemoveAsync(postId, default)).Should().BeTrue();

        // The global filter hides the row from the second call, so it finds nothing and says so
        // rather than reporting a second success an operator would read as a fresh takedown.
        (await service.RemoveAsync(postId, default))
            .Should()
            .BeFalse();
    }

    [Fact]
    public async Task APostThatDoesNotExistAnswersFalse()
    {
        DbContextOptions<VortexDbContext> options = NewOptions();

        (await new FeedAdminService(new TestDbContextFactory(options)).RemoveAsync(404, default))
            .Should()
            .BeFalse();
    }

    private static DbContextOptions<VortexDbContext> NewOptions() =>
        new DbContextOptionsBuilder<VortexDbContext>()
            .UseInMemoryDatabase($"feed-admin-{Guid.NewGuid():N}")
            .Options;

    private static async Task<int> SeedPostAsync(
        DbContextOptions<VortexDbContext> options,
        int playerId
    )
    {
        await using VortexDbContext db = new(options);

        WebFeedPostEntity post = new()
        {
            PlayerEntityId = playerId,
            Kind = FeedPostKind.Status,
            Body = "coucou",
        };

        db.WebFeedPosts.Add(post);
        await db.SaveChangesAsync();

        return post.Id;
    }

    private sealed class TestDbContextFactory(DbContextOptions<VortexDbContext> options)
        : IDbContextFactory<VortexDbContext>
    {
        public VortexDbContext CreateDbContext() => new(options);
    }
}
