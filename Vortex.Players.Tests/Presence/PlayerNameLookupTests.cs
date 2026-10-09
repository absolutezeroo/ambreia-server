using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vortex.Database.Context;
using Vortex.Database.Entities.Players;
using Vortex.Players.Configuration;
using Vortex.Players.Grains;
using Vortex.Players.Tests.Support;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Enums;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Players.Tests.Presence;

/// <summary>
/// An id nobody holds is an answer, not a fault. The single-id path went through <c>FirstAsync</c>,
/// so one unknown id threw at whoever asked -- after an error-level log -- where the many-ids path
/// simply left the id out.
/// </summary>
public sealed class PlayerNameLookupTests
{
    private static readonly PlayerId Unknown = new(999);

    [Fact]
    public async Task AnUnknownId_HasNoName()
    {
        PlayerDirectoryGrain grain = Create(Options());

        (await grain.GetPlayerNameAsync(Unknown, CancellationToken.None).ConfigureAwait(true))
            .Should()
            .BeEmpty();
    }

    [Fact]
    public async Task AnUnknownId_AskedAlone_IsLeftOut()
    {
        PlayerDirectoryGrain grain = Create(Options());

        ImmutableDictionary<PlayerId, string> names = await grain
            .GetPlayerNamesAsync([Unknown], CancellationToken.None)
            .ConfigureAwait(true);

        names.Should().BeEmpty();
    }

    /// <summary>
    /// The grain is <c>[KeepAlive]</c>: an unbounded cache would hold every player ever looked up for
    /// the lifetime of the silo. A forgotten name is only one more database read.
    /// </summary>
    [Fact]
    public async Task TheNameCache_NeverGrowsPastItsCap()
    {
        DbContextOptions<VortexDbContext> options = Options();
        await SeedAsync(options, "Ann", "Bob", "Cid", "Dee");
        PlayerDirectoryGrain grain = Create(options, maxCachedNames: 2);

        for (int id = 1; id <= 4; id++)
        {
            await grain.GetPlayerNameAsync(new PlayerId(id), CancellationToken.None);
        }

        grain.CachedNames.Should().BeLessThanOrEqualTo(2);
        (await grain.GetPlayerNameAsync(new PlayerId(1), CancellationToken.None))
            .Should()
            .Be("Ann", "a name evicted from the cache is read again");
    }

    [Fact]
    public async Task AName_FindsItsPlayer()
    {
        DbContextOptions<VortexDbContext> options = Options();
        await SeedAsync(options, "Ann", "Bob");

        (await Create(options).GetPlayerIdByNameAsync("Bob", CancellationToken.None))
            .Should()
            .Be(new PlayerId(2));
    }

    /// <summary>Spec F3c §8: case aside, like the unique index on players.name; the name found is
    /// cached as stored.</summary>
    [Fact]
    public async Task AName_IsFoundWhateverItsCase()
    {
        DbContextOptions<VortexDbContext> options = Options();
        await SeedAsync(options, "Ann", "Bob");
        PlayerDirectoryGrain grain = Create(options);

        (await grain.GetPlayerIdByNameAsync("bOB", CancellationToken.None))
            .Should()
            .Be(new PlayerId(2));
        grain.CachedNames.Should().Be(1);
        (await grain.GetPlayerNameAsync(new PlayerId(2), CancellationToken.None))
            .Should()
            .Be("Bob");
    }

    [Theory]
    [InlineData("Zed")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AnUnknownOrBlankName_FindsNobody(string name)
    {
        DbContextOptions<VortexDbContext> options = Options();
        await SeedAsync(options, "Ann", "Bob");

        (await Create(options).GetPlayerIdByNameAsync(name, CancellationToken.None))
            .Should()
            .BeNull();
    }

    private static DbContextOptions<VortexDbContext> Options() =>
        new DbContextOptionsBuilder<VortexDbContext>()
            .UseInMemoryDatabase($"directory-{Guid.NewGuid():N}")
            .Options;

    private static async Task SeedAsync(
        DbContextOptions<VortexDbContext> options,
        params string[] names
    )
    {
        await using VortexDbContext db = new(options);

        for (int i = 0; i < names.Length; i++)
        {
            db.Players.Add(
                new PlayerEntity
                {
                    Id = i + 1,
                    Name = names[i],
                    Figure = "hd-180-1",
                    Gender = AvatarGenderType.Male,
                    PlayerStatus = PlayerStatusType.Offline,
                    PlayerPerks = PlayerPerkFlags.None,
                }
            );
        }

        await db.SaveChangesAsync();
    }

    private static PlayerDirectoryGrain Create(
        DbContextOptions<VortexDbContext> options,
        int maxCachedNames = 10_000
    ) =>
        GrainActivationContext.CreateWithIntegerKey<PlayerDirectoryGrain>(
            0,
            new OptionsDbContextFactory(options),
            Microsoft.Extensions.Options.Options.Create(
                new PlayerDirectoryConfig { MaxCachedNames = maxCachedNames }
            ),
            NullLogger<PlayerDirectoryGrain>.Instance
        );
}
