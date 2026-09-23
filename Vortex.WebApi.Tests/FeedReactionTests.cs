using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Database.Context;
using Vortex.Database.Entities.Players;
using Vortex.Primitives.Feed;
using Vortex.Primitives.Players.Enums;
using Vortex.Primitives.Rooms.Enums;
using Vortex.WebApi.Services;
using Xunit;

namespace Vortex.WebApi.Tests;

/// <summary>
/// Reposting and favouriting.
/// </summary>
/// <remarks>
/// <para>
/// The route takes the WANTED STATE, never a toggle. A toggle sent twice — a flaky connection
/// retrying, a double tap — lands on the opposite of what the reader pressed, and the reader has
/// no way to tell which of the two happened. With the state in the body a retry is a no-op, which
/// is what <see cref="ReactingTwiceLeavesOneReaction"/> pins.
/// </para>
/// <para>
/// Withdrawing DELETES the row rather than soft-deleting it. A soft delete would keep the pair and
/// the unique index on (post, player, kind) would then refuse the next honest reaction — the one
/// place in this feature where <c>DeletedAt</c> is the wrong tool, as
/// <c>WebFeedReactionEntity</c> says at the declaration.
/// </para>
/// </remarks>
public sealed class FeedReactionTests
{
    [Fact]
    public async Task ReactingTwiceLeavesOneReaction()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Prodiigy");
        FeedPost post = await StatusAsync(factory, 1);

        await Service(factory)
            .SetReactionAsync(1, post.Id, FeedReactionKind.Favorite, true, default);

        FeedPost? after = await Service(factory)
            .SetReactionAsync(1, post.Id, FeedReactionKind.Favorite, true, default);

        after!
            .FavoriteCount.Should()
            .Be(1, "the body carries the wanted state, so a retry is a no-op");
        after.FavoritedByMe.Should().BeTrue();
    }

    [Fact]
    public async Task WithdrawingRemovesIt()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Prodiigy");
        FeedPost post = await StatusAsync(factory, 1);

        await Service(factory).SetReactionAsync(1, post.Id, FeedReactionKind.Repost, true, default);

        FeedPost? after = await Service(factory)
            .SetReactionAsync(1, post.Id, FeedReactionKind.Repost, false, default);

        after!.RepostCount.Should().Be(0);
        after.RepostedByMe.Should().BeFalse();
    }

    [Fact]
    public async Task WithdrawingTwiceIsAlsoANoOp()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Prodiigy");
        FeedPost post = await StatusAsync(factory, 1);

        // Never reacted in the first place. The remove branch has to tolerate finding nothing,
        // or the first stray "off" from a reloaded page throws.
        FeedPost? after = await Service(factory)
            .SetReactionAsync(1, post.Id, FeedReactionKind.Favorite, false, default);

        after!.FavoriteCount.Should().Be(0);
        after.FavoritedByMe.Should().BeFalse();
    }

    [Fact]
    public async Task TheTwoKindsAreIndependent()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Prodiigy");
        FeedPost post = await StatusAsync(factory, 1);

        await Service(factory).SetReactionAsync(1, post.Id, FeedReactionKind.Repost, true, default);

        FeedPost? after = await Service(factory)
            .SetReactionAsync(1, post.Id, FeedReactionKind.Favorite, true, default);

        // The unique index spans (post, player, KIND); if the service ever matched on the pair
        // alone, the favourite would move the repost instead of joining it.
        after!.RepostCount.Should().Be(1);
        after.FavoriteCount.Should().Be(1);
    }

    [Fact]
    public async Task TwoPlayersEachCount()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Prodiigy");
        await PlayerAsync(factory, 2, "Synes");
        FeedPost post = await StatusAsync(factory, 1);

        await Service(factory)
            .SetReactionAsync(1, post.Id, FeedReactionKind.Favorite, true, default);

        FeedPost? after = await Service(factory)
            .SetReactionAsync(2, post.Id, FeedReactionKind.Favorite, true, default);

        after!.FavoriteCount.Should().Be(2);
        after.FavoritedByMe.Should().BeTrue("viewer 2 is the one who just reacted");
    }

    [Fact]
    public async Task AReactionOnAPostThatIsNotThereIsRefused()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Prodiigy");

        (await Service(factory).SetReactionAsync(1, 404, FeedReactionKind.Favorite, true, default))
            .Should()
            .BeNull();
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
