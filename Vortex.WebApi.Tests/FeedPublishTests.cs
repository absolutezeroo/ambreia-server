using System.Collections.Generic;
using System.Linq;
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

    private static IWebApiFeedService Service(WebApiTestFactory factory) =>
        new WebApiFeedService(factory.DbContexts);

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
