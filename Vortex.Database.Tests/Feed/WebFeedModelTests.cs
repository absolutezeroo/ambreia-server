using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Vortex.Database.Context;
using Vortex.Database.Entities.Feed;
using Vortex.Primitives.Feed;
using Xunit;

namespace Vortex.Database.Tests.Feed;

/// <summary>
/// The shape of the feed's tables, asserted rather than assumed.
/// </summary>
/// <remarks>
/// The unique indexes here are the whole correctness story for voting and reacting — one vote per
/// player per poll, one reaction per player per post per kind — and EF's in-memory provider does
/// not enforce a unique index, so nothing else in the suite would notice one being dropped in a
/// later edit. Reading them off the model is how they stay asserted without a MySQL in CI.
/// </remarks>
public sealed class WebFeedModelTests
{
    private static VortexDbContext Context() =>
        new(
            new DbContextOptionsBuilder<VortexDbContext>()
                .UseInMemoryDatabase($"feed-model-{System.Guid.NewGuid():N}")
                .Options
        );

    [Fact]
    public void Posts_AreSelfReferencing()
    {
        using VortexDbContext db = Context();

        IEntityType post = db.Model.FindEntityType(typeof(WebFeedPostEntity))!;

        // GetTableName() is asserted off the [Table] attribute directly, not off the in-memory
        // model: the in-memory provider carries no relational conventions, so GetTableName() falls
        // back to the CLR name for every entity in this project (see the identical note in
        // EntityChangeInterceptorTests) and would pass this assertion for the wrong reason no
        // matter what the table is actually named.
        typeof(WebFeedPostEntity)
            .GetCustomAttribute<TableAttribute>()!
            .Name.Should()
            .Be("web_feed_posts");
        post.FindProperty(nameof(WebFeedPostEntity.ParentEntityId))!
            .IsNullable.Should()
            .BeTrue("a root post has no parent, and that is what makes a comment a post");
    }

    [Fact]
    public void OneVotePerPlayerPerPoll()
    {
        using VortexDbContext db = Context();

        db.Model.FindEntityType(typeof(WebFeedPollVoteEntity))!
            .GetIndexes()
            .Should()
            .ContainSingle(i =>
                i.IsUnique
                && i.Properties.Select(p => p.Name)
                    .SequenceEqual(new[] { "WebFeedPostEntityId", "PlayerEntityId" })
            );
    }

    [Fact]
    public void OneReactionPerPlayerPerPostPerKind()
    {
        using VortexDbContext db = Context();

        db.Model.FindEntityType(typeof(WebFeedReactionEntity))!
            .GetIndexes()
            .Should()
            .ContainSingle(i =>
                i.IsUnique
                && i.Properties.Select(p => p.Name)
                    .SequenceEqual(new[] { "WebFeedPostEntityId", "PlayerEntityId", "Kind" })
            );
    }

    [Fact]
    public async System.Threading.Tasks.Task AWholePostRoundTrips()
    {
        await using VortexDbContext db = Context();

        WebFeedPostEntity root = new()
        {
            PlayerEntityId = 1,
            Kind = FeedPostKind.Poll,
            Body = "Pizza ou burger ?",
        };
        db.WebFeedPosts.Add(root);
        await db.SaveChangesAsync();

        db.WebFeedPollChoices.Add(
            new WebFeedPollChoiceEntity
            {
                WebFeedPostEntityId = root.Id,
                Ordinal = 0,
                Label = "Pizza",
            }
        );
        db.WebFeedPosts.Add(
            new WebFeedPostEntity
            {
                PlayerEntityId = 2,
                Kind = FeedPostKind.Status,
                Body = "les deux",
                ParentEntityId = root.Id,
            }
        );
        await db.SaveChangesAsync();

        (await db.WebFeedPosts.CountAsync(p => p.ParentEntityId == root.Id)).Should().Be(1);
        (await db.WebFeedPollChoices.CountAsync(c => c.WebFeedPostEntityId == root.Id))
            .Should()
            .Be(1);
    }
}
