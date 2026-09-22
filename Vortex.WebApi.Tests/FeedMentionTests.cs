using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Vortex.Database.Context;
using Vortex.Database.Entities.Messenger;
using Vortex.Database.Entities.Players;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Feed;
using Vortex.Primitives.Notifications;
using Vortex.Primitives.Players.Enums;
using Vortex.Primitives.Rooms.Enums;
using Vortex.WebApi.Notifications;
using Vortex.WebApi.Services;
using Xunit;

namespace Vortex.WebApi.Tests;

/// <summary>
/// <c>@name</c> in a post: who it announces, and the three cases where it announces nobody.
/// </summary>
/// <remarks>
/// Two halves, tested as two. The feed's job is to RESOLVE the names and raise an event per real
/// player; the handler's job is to decide whether that becomes a bell. Keeping them apart is what
/// lets the block rule live with the notifications and not in the feed.
/// </remarks>
public sealed class FeedMentionTests
{
    [Fact]
    public async Task AMentionAnnouncesThePersonNamed()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Prodiigy");
        await PlayerAsync(factory, 2, "Chloe");

        await StatusAsync(factory, 1, "coucou @Chloe ça va ?");

        FeedMentionedEvent mention = factory
            .Events.Of<FeedMentionedEvent>()
            .Should()
            .ContainSingle()
            .Subject;

        mention.ActorPlayerId.Should().Be(1);
        mention.MentionedPlayerId.Should().Be(2);
    }

    /// <summary>
    /// A name nobody has raises nothing at all — which is the entire reason the server resolves
    /// names when the page does not. The text still renders it as a link.
    /// </summary>
    [Fact]
    public async Task AnUnknownNameAnnouncesNobody()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Prodiigy");

        await StatusAsync(factory, 1, "salut @PersonneIci");

        factory.Events.Of<FeedMentionedEvent>().Should().BeEmpty();
    }

    /// <summary>
    /// Naming the same person twice in different capitals is still one person.
    /// </summary>
    /// <remarks>
    /// The deduplication is ours and is tested here. Whether <c>@chloe</c> RESOLVES to Chloe is
    /// not: that is a plain <c>==</c> against the column, so the answer is the collation's, and
    /// the in-memory provider these tests run on does not have MySQL's. See FeedMentions.Extract.
    /// </remarks>
    [Fact]
    public async Task TheSamePersonInDifferentCapitalsIsOnePerson()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Prodiigy");
        await PlayerAsync(factory, 2, "Chloe");

        await StatusAsync(factory, 1, "hé @Chloe, oui @CHLOE, toi @chloe");

        // One event at most, from whichever spelling the provider matched -- never three.
        factory.Events.Of<FeedMentionedEvent>().Should().HaveCountLessThanOrEqualTo(1);
    }

    /// <summary>
    /// The cap. Eleven names, ten pings — the eleventh still renders as a link, it simply does not
    /// ring. Without this a single post is a megaphone.
    /// </summary>
    [Fact]
    public async Task OnePostCannotPingTheWholeHotel()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Prodiigy");

        string body = "";

        for (int n = 0; n < FeedMentions.MaximumPerPost + 1; n++)
        {
            await PlayerAsync(factory, 100 + n, $"Cible{n}");
            body += $"@Cible{n} ";
        }

        await StatusAsync(factory, 1, body);

        factory.Events.Of<FeedMentionedEvent>().Should().HaveCount(FeedMentions.MaximumPerPost);
    }

    /// <summary>The same name twice in one post is one person, so one ping.</summary>
    [Fact]
    public async Task NamingSomebodyTwiceRingsOnce()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Prodiigy");
        await PlayerAsync(factory, 2, "Chloe");

        await StatusAsync(factory, 1, "@Chloe et encore @Chloe");

        factory.Events.Of<FeedMentionedEvent>().Should().ContainSingle();
    }

    /// <summary>
    /// Mentioning yourself announces the event but rings nothing: the sink drops a row whose actor
    /// is its own recipient, once, for all thirty kinds.
    /// </summary>
    [Fact]
    public async Task MentioningMyselfRingsNothing()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Prodiigy");

        await StatusAsync(factory, 1, "moi c'est @Prodiigy");

        FeedMentionedEvent mention = factory
            .Events.Of<FeedMentionedEvent>()
            .Should()
            .ContainSingle()
            .Subject;

        await Handler(factory).HandleAsync(mention, Context(), default);

        (await Service(factory).GetAsync(1, 1, 0)).Items.Should().BeEmpty();
    }

    /// <summary>
    /// Somebody I blocked cannot make my bell ring. The mention stays in their text — this does
    /// not censor anybody — it simply does not reach me.
    /// </summary>
    [Fact]
    public async Task SomebodyIBlockedCannotPingMe()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Genant");
        await PlayerAsync(factory, 2, "Chloe");
        await BlockAsync(factory, blocker: 2, blocked: 1);

        await StatusAsync(factory, 1, "hé @Chloe");

        FeedMentionedEvent mention = factory
            .Events.Of<FeedMentionedEvent>()
            .Should()
            .ContainSingle()
            .Subject;

        await Handler(factory).HandleAsync(mention, Context(), default);

        (await Service(factory).GetAsync(2, 1, 0)).Items.Should().BeEmpty();
    }

    /// <summary>And the same mention reaches somebody who has not blocked them.</summary>
    [Fact]
    public async Task AMentionReachesAnyoneWhoHasNotBlockedMe()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Prodiigy");
        await PlayerAsync(factory, 2, "Chloe");

        await StatusAsync(factory, 1, "hé @Chloe");

        await Handler(factory)
            .HandleAsync(factory.Events.Of<FeedMentionedEvent>().Single(), Context(), default);

        NotificationPage page = await Service(factory).GetAsync(2, 1, 0);

        page.Items.Should().ContainSingle();
        page.Items[0].Kind.Should().Be(NotificationKind.FeedMentioned);
        page.Items[0].Actor!.Name.Should().Be("Prodiigy");
    }

    /// <summary>Commenting on somebody's post tells its author, and names the thread to open.</summary>
    [Fact]
    public async Task CommentingTellsTheAuthor()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Prodiigy");
        await PlayerAsync(factory, 2, "Chloe");

        FeedPost root = await StatusAsync(factory, 1, "mon message");

        await Feed(factory)
            .PublishAsync(
                2,
                new FeedPublishRequest(
                    Kind: FeedPostKind.Status,
                    Body: "bien vu",
                    PhotoKeys: null,
                    PollChoices: null,
                    ParentId: root.Id,
                    GroupId: null
                ),
                default
            );

        FeedCommentedEvent comment = factory
            .Events.Of<FeedCommentedEvent>()
            .Should()
            .ContainSingle()
            .Subject;

        comment.AuthorPlayerId.Should().Be(1);
        comment.ActorPlayerId.Should().Be(2);
        comment.PostId.Should().Be(root.Id);
    }

    /// <summary>
    /// A repost announces once. Pressing it again finds the row already there and falls through,
    /// so a flaky connection cannot ring the same bell twice.
    /// </summary>
    [Fact]
    public async Task ARepostAnnouncesOnceHoweverOftenItIsSent()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Prodiigy");
        await PlayerAsync(factory, 2, "Chloe");

        FeedPost root = await StatusAsync(factory, 1, "mon message");

        await Feed(factory).SetReactionAsync(2, root.Id, FeedReactionKind.Repost, true, default);
        await Feed(factory).SetReactionAsync(2, root.Id, FeedReactionKind.Repost, true, default);

        factory.Events.Of<FeedRepostedEvent>().Should().ContainSingle();

        // A favourite is not a repost and announces nothing.
        await Feed(factory).SetReactionAsync(2, root.Id, FeedReactionKind.Favorite, true, default);

        factory.Events.Of<FeedRepostedEvent>().Should().ContainSingle();
    }

    /// <summary>
    /// A like announces itself, exactly as a repost does.
    /// </summary>
    /// <remarks>
    /// It did not, for a while: the first three feed events were written to a list of themes that
    /// named the mention, the comment and the share, and the commonest interaction of the four
    /// was simply absent. The repost beside it worked, which is what made it invisible.
    /// </remarks>
    [Fact]
    public async Task ALikeAnnouncesItselfLikeAShareDoes()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Prodiigy");
        await PlayerAsync(factory, 2, "Chloe");

        FeedPost root = await StatusAsync(factory, 1, "mon message");

        await Feed(factory).SetReactionAsync(2, root.Id, FeedReactionKind.Favorite, true, default);

        FeedFavouritedEvent like = factory
            .Events.Of<FeedFavouritedEvent>()
            .Should()
            .ContainSingle()
            .Subject;

        like.ActorPlayerId.Should().Be(2);
        like.AuthorPlayerId.Should().Be(1);
        like.PostId.Should().Be(root.Id);

        // And only once, however many times the button is pressed.
        await Feed(factory).SetReactionAsync(2, root.Id, FeedReactionKind.Favorite, true, default);

        factory.Events.Of<FeedFavouritedEvent>().Should().ContainSingle();
    }

    /// <summary>
    /// Every write on the feed says the thread moved, and always names the ROOT — that is the
    /// card an open page has to refresh.
    /// </summary>
    [Fact]
    public async Task EveryWriteAnnouncesTheThreadThatMoved()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Prodiigy");
        await PlayerAsync(factory, 2, "Chloe");

        FeedPost root = await StatusAsync(factory, 1, "mon message");

        // Publishing the root is itself a change.
        factory
            .Events.Of<FeedChangedEvent>()
            .Should()
            .ContainSingle()
            .Which.RootPostId.Should()
            .Be(root.Id);

        // A COMMENT names the root, not itself.
        FeedPost comment = (
            await Feed(factory)
                .PublishAsync(
                    2,
                    new FeedPublishRequest(
                        Kind: FeedPostKind.Status,
                        Body: "bien vu",
                        PhotoKeys: null,
                        PollChoices: null,
                        ParentId: root.Id,
                        GroupId: null
                    ),
                    default
                )
        ).Post!;

        factory.Events.Of<FeedChangedEvent>().Should().HaveCount(2);
        factory.Events.Of<FeedChangedEvent>()[1].RootPostId.Should().Be(root.Id);

        // A REPLY is two levels down and still names the root.
        await Feed(factory)
            .PublishAsync(
                1,
                new FeedPublishRequest(
                    Kind: FeedPostKind.Status,
                    Body: "merci",
                    PhotoKeys: null,
                    PollChoices: null,
                    ParentId: comment.Id,
                    GroupId: null
                ),
                default
            );

        factory.Events.Of<FeedChangedEvent>()[2].RootPostId.Should().Be(root.Id);

        // A reaction, including one being WITHDRAWN: the count moved on everybody's copy.
        await Feed(factory).SetReactionAsync(2, root.Id, FeedReactionKind.Favorite, true, default);
        await Feed(factory).SetReactionAsync(2, root.Id, FeedReactionKind.Favorite, false, default);

        factory.Events.Of<FeedChangedEvent>().Should().HaveCount(5);

        // And a delete.
        await Feed(factory).DeleteAsync(1, root.Id, default);

        factory.Events.Of<FeedChangedEvent>().Should().HaveCount(6);
        factory.Events.Of<FeedChangedEvent>()[5].RootPostId.Should().Be(root.Id);
    }

    private static EventContext Context() => new() { CorrelationId = "test" };

    private static FeedMentionNotificationHandler Handler(WebApiTestFactory factory) =>
        new(
            new NotificationSink(
                factory.DbContexts,
                factory.Broadcast,
                NullLogger<NotificationSink>.Instance
            ),
            factory.DbContexts
        );

    private static IWebApiNotificationService Service(WebApiTestFactory factory) =>
        new WebApiNotificationService(factory.DbContexts);

    private static IWebApiFeedService Feed(WebApiTestFactory factory) =>
        new WebApiFeedService(factory.DbContexts, factory.Events);

    private static async Task<FeedPost> StatusAsync(
        WebApiTestFactory factory,
        int playerId,
        string body
    ) =>
        (
            await Feed(factory)
                .PublishAsync(
                    playerId,
                    new FeedPublishRequest(FeedPostKind.Status, body, null, null, null, null),
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

    private static async Task BlockAsync(WebApiTestFactory factory, int blocker, int blocked)
    {
        await using VortexDbContext db = await factory.DbContexts.CreateDbContextAsync();

        db.MessengerBlocked.Add(
            new MessengerBlockedEntity
            {
                PlayerEntityId = blocker,
                BlockedPlayerEntityId = blocked,
                PlayerEntity = await db.Players.FindAsync(blocker) ?? null!,
                BlockedPlayerEntity = await db.Players.FindAsync(blocked) ?? null!,
            }
        );

        await db.SaveChangesAsync();
    }
}
