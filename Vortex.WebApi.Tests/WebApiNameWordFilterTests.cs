using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans;
using Vortex.Database.Context;
using Vortex.Primitives.Moderation.WordFilter;
using Vortex.WebApi.Configuration;
using Vortex.WebApi.Services;
using Xunit;

namespace Vortex.WebApi.Tests;

/// <summary>
/// The hotel-wide word filter on the website's three name doors: the availability probe, the
/// avatar creation and the rename. Each is a second door onto the in-game name, so each has to
/// refuse what the in-game rename refuses.
/// </summary>
public sealed class WebApiNameWordFilterTests
{
    // Refused, as the real filter answers any hit on a Username (WordFilterTests pins that rule).
    private readonly FakeWordFilter _filter = new()
    {
        Answer = text => new WordFilterResult(text, text == "Connard", false),
    };

    [Fact]
    public async Task TheProbe_SaysAFilteredNameIsNotAvailable_WithoutJournaling()
    {
        (await Service().NameAvailableAsync("Connard", CancellationToken.None)).Should().BeFalse();
        (await Service().NameAvailableAsync("Gentil", CancellationToken.None)).Should().BeTrue();

        _filter.Applied.Should().BeEmpty();
    }

    [Fact]
    public async Task CreatingAnAvatar_RefusesAFilteredName_AndJournalsItAgainstTheAccount()
    {
        (bool success, int playerId, string? error) = await Service()
            .CreateAvatarAsync(7, "Connard", "hr-115", "M", CancellationToken.None);

        success.Should().BeFalse();
        playerId.Should().Be(0);
        error.Should().Be("pocket.auth.name_not_valid");
        _filter
            .Applied.Should()
            .ContainSingle()
            .Which.Should()
            .Be(("Connard", WordFilterSource.Username, 7));
    }

    [Fact]
    public async Task Renaming_RefusesAFilteredName()
    {
        (await Service().SetNameAsync(12, "Connard", CancellationToken.None)).Should().BeFalse();

        _filter
            .Applied.Should()
            .ContainSingle()
            .Which.Should()
            .Be(("Connard", WordFilterSource.Username, 12));
    }

    private WebApiPlayerService Service() =>
        new(
            new TestDbContextFactory(
                new DbContextOptionsBuilder<VortexDbContext>()
                    .UseInMemoryDatabase($"webapi-names-{Guid.NewGuid():N}")
                    .Options
            ),
            DispatchProxy.Create<IGrainFactory, UnusedGrainFactory>(),
            _filter,
            Options.Create(new WebApiConfig()),
            NullLogger<WebApiPlayerService>.Instance
        );

    private sealed class TestDbContextFactory(DbContextOptions<VortexDbContext> options)
        : IDbContextFactory<VortexDbContext>
    {
        public VortexDbContext CreateDbContext() => new(options);

        public Task<VortexDbContext> CreateDbContextAsync(CancellationToken ct = default) =>
            Task.FromResult(CreateDbContext());
    }

    /// <summary>Every door here refuses before any grain is reached; one that did not would throw.</summary>
    private class UnusedGrainFactory : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException(
                $"The name refusal reached the grain factory ({targetMethod?.Name})."
            );
    }
}
