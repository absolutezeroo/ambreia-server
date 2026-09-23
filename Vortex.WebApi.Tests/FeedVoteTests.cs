using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Context;
using Vortex.Database.Entities.Feed;
using Vortex.Database.Entities.Players;
using Vortex.Primitives.Feed;
using Vortex.Primitives.Players.Enums;
using Vortex.Primitives.Rooms.Enums;
using Vortex.WebApi.Services;
using Xunit;

namespace Vortex.WebApi.Tests;

/// <summary>
/// Voting in a feed poll.
/// </summary>
/// <remarks>
/// <para>
/// One rule carries this file: <b>one player is one voice, however many times they press.</b> The
/// unique index on (post, player) says so at the database, but EF's in-memory provider does not
/// enforce a unique index, so nothing here would notice the guard disappearing from the service —
/// which is exactly why the service has to hold the rule in code as well, and why
/// <see cref="VotingTwiceMovesTheVoteRatherThanAddingOne"/> asserts the TOTAL rather than just the
/// player's own choice.
/// </para>
/// <para>
/// The second rule is quieter and easier to lose: a choice id is a number the browser sends, and
/// without checking that it belongs to the poll named in the route, a vote can be aimed at any
/// choice in the hotel and would land silently.
/// </para>
/// </remarks>
public sealed class FeedVoteTests
{
    [Fact]
    public async Task AVoteCounts()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Synes");
        FeedPost poll = await PollAsync(factory, 1);

        FeedPost? after = await Service(factory)
            .VoteAsync(1, poll.Id, poll.Poll!.Choices[0].Id, default);

        after!.Poll!.TotalVotes.Should().Be(1);
        after.Poll.MyChoiceId.Should().Be(poll.Poll.Choices[0].Id);
        after.Poll.Choices.Single(c => c.Id == poll.Poll.Choices[0].Id).Votes.Should().Be(1);
    }

    [Fact]
    public async Task VotingTwiceMovesTheVoteRatherThanAddingOne()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Synes");
        FeedPost poll = await PollAsync(factory, 1);

        await Service(factory).VoteAsync(1, poll.Id, poll.Poll!.Choices[0].Id, default);

        FeedPost? after = await Service(factory)
            .VoteAsync(1, poll.Id, poll.Poll.Choices[1].Id, default);

        after!
            .Poll!.TotalVotes.Should()
            .Be(1, "one player is one voice, however many times they press");
        after.Poll.MyChoiceId.Should().Be(poll.Poll.Choices[1].Id);
        after.Poll.Choices.Single(c => c.Id == poll.Poll.Choices[0].Id).Votes.Should().Be(0);
    }

    [Fact]
    public async Task TwoPlayersEachGetAVoice()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Synes");
        await PlayerAsync(factory, 2, "Prodiigy");
        FeedPost poll = await PollAsync(factory, 1);

        await Service(factory).VoteAsync(1, poll.Id, poll.Poll!.Choices[0].Id, default);

        FeedPost? after = await Service(factory)
            .VoteAsync(2, poll.Id, poll.Poll.Choices[0].Id, default);

        // The mirror of the test above: the guard must be per player, not a blanket "one vote".
        after!.Poll!.TotalVotes.Should().Be(2);
        after.Poll.Choices.Single(c => c.Id == poll.Poll.Choices[0].Id).Votes.Should().Be(2);
    }

    [Fact]
    public async Task AChoiceFromAnotherPollIsRefused()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Synes");
        FeedPost a = await PollAsync(factory, 1);
        FeedPost b = await PollAsync(factory, 1);

        (await Service(factory).VoteAsync(1, a.Id, b.Poll!.Choices[0].Id, default))
            .Should()
            .BeNull();
    }

    [Fact]
    public async Task AStatusPostCannotBeVotedOn()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Synes");
        FeedPost poll = await PollAsync(factory, 1);
        FeedPost status = (
            await Service(factory)
                .PublishAsync(
                    1,
                    new FeedPublishRequest(FeedPostKind.Status, "coucou", null, null, null, null),
                    default
                )
        ).Post!;

        // A choice that exists, aimed at a post that is not a poll: the guard has to reject on the
        // post as well as on the choice, or the vote lands on a row nothing will ever render.
        (await Service(factory).VoteAsync(1, status.Id, poll.Poll!.Choices[0].Id, default))
            .Should()
            .BeNull();
    }

    [Fact]
    public async Task AVoteOnADeletedPollIsRefused()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Synes");
        FeedPost poll = await PollAsync(factory, 1);

        // Soft-deleted through the context rather than through `DeleteAsync`, which arrives in a
        // later task: what is under test is that `VoteAsync` goes through `Readable(db)`, not how
        // the row came to be deleted.
        await using (VortexDbContext db = await factory.DbContexts.CreateDbContextAsync())
        {
            WebFeedPostEntity row = (await db.WebFeedPosts.FindAsync(poll.Id))!;

            row.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        (await Service(factory).VoteAsync(1, poll.Id, poll.Poll!.Choices[0].Id, default))
            .Should()
            .BeNull();
    }

    private static IWebApiFeedService Service(WebApiTestFactory factory) =>
        new WebApiFeedService(factory.DbContexts, factory.Events, factory.WordFilter);

    private static async Task<FeedPost> PollAsync(WebApiTestFactory factory, int playerId) =>
        (
            await Service(factory)
                .PublishAsync(
                    playerId,
                    new FeedPublishRequest(
                        FeedPostKind.Poll,
                        "Pizza ou burger ?",
                        null,
                        ["Pizza", "Burger"],
                        null,
                        null
                    ),
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
