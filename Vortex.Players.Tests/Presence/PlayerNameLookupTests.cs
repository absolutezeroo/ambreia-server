using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Vortex.Database.Context;
using Vortex.Players.Grains;
using Vortex.Primitives.Players;
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
        PlayerDirectoryGrain grain = Create();

        (await grain.GetPlayerNameAsync(Unknown, CancellationToken.None).ConfigureAwait(true))
            .Should()
            .BeEmpty();
    }

    [Fact]
    public async Task AnUnknownId_AskedAlone_IsLeftOut()
    {
        PlayerDirectoryGrain grain = Create();

        ImmutableDictionary<PlayerId, string> names = await grain
            .GetPlayerNamesAsync([Unknown], CancellationToken.None)
            .ConfigureAwait(true);

        names.Should().BeEmpty();
    }

    private static PlayerDirectoryGrain Create()
    {
        DbContextOptions<VortexDbContext> options = new DbContextOptionsBuilder<VortexDbContext>()
            .UseInMemoryDatabase($"directory-{Guid.NewGuid():N}")
            .Options;

        return GrainActivationContext.CreateWithIntegerKey<PlayerDirectoryGrain>(
            0,
            new InMemoryFactory(options),
            NullLogger<PlayerDirectoryGrain>.Instance
        );
    }

    private sealed class InMemoryFactory(DbContextOptions<VortexDbContext> options)
        : IDbContextFactory<VortexDbContext>
    {
        public VortexDbContext CreateDbContext() => new(options);
    }
}
