using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
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
