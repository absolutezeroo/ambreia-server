using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Orleans;
using Vortex.Dashboard.API.Api.Hotel;
using Vortex.Dashboard.API.Api.Hotel.Contracts;
using Vortex.Dashboard.API.Infrastructure;
using Vortex.Database.Context;
using Vortex.Database.Entities.Moderation;
using Vortex.Observability.Configuration;
using Vortex.Primitives.Moderation.WordFilter;
using Vortex.Primitives.Server.Grains;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Dashboard.Tests;

/// <summary>
/// The drawer's "try a sentence" box. Its whole point is trying the word being written before it is
/// saved, so it runs the saved list PLUS the draft — and, when a word is being edited, the draft in
/// place of that word.
/// </summary>
public sealed class WordFilterReadsTests
{
    private readonly DbContextOptions<VortexDbContext> _options =
        new DbContextOptionsBuilder<VortexDbContext>()
            .UseInMemoryDatabase($"wordfilter-reads-{Guid.NewGuid():N}")
            .Options;

    [Fact]
    public async Task TheDraftIsTried_BesideTheSavedList()
    {
        await SeedAsync("nul", WordFilterAction.Refuse);

        WordFilterReads reads = Reads();
        WordFilterDraft draft = new(
            null,
            "con",
            WordFilterAction.Replace,
            WordFilterMatch.WholeWord,
            null
        );

        (await reads.TestAsync("t'es con", draft, default))
            .Should()
            .Be(new WordFilterTestResult("t'es bobba", false, true));
        (await reads.TestAsync("t'es nul", draft, default)).Refused.Should().BeTrue();
    }

    [Fact]
    public async Task AnEditedWordIsTried_InPlaceOfItsSavedSelf()
    {
        int id = await SeedAsync("nul", WordFilterAction.Refuse);

        WordFilterDraft draft = new(
            id,
            "zut",
            WordFilterAction.Replace,
            WordFilterMatch.WholeWord,
            "***"
        );

        (await Reads().TestAsync("nul et zut", draft, default))
            .Should()
            .Be(new WordFilterTestResult("nul et ***", false, true));
    }

    [Fact]
    public async Task WithNoDraft_TheLiveFilterAnswers()
    {
        (await Reads().TestAsync("anything", null, default))
            .Should()
            .Be(new WordFilterTestResult("live", true, false));
    }

    private WordFilterReads Reads() =>
        new(
            new TestDbContextFactory(_options),
            new DashboardAssetUrls(Options.Create(new ObservabilityConfig())),
            FakeProxy.Create<IWordFilter>(call =>
                call.Method.Name == nameof(IWordFilter.Test)
                    ? new WordFilterResult("live", true, false)
                    : null
            ),
            // No operator override of the replacement: the config grain answers nothing.
            FakeProxy.Create<IGrainFactory>(call =>
                call.Method.Name == nameof(IGrainFactory.GetGrain)
                    ? FakeProxy.Create<IServerConfigGrain>(_ => null)
                    : null
            )
        );

    private async Task<int> SeedAsync(string word, WordFilterAction action)
    {
        await using VortexDbContext db = new(_options);

        WordFilterWordEntity row = new()
        {
            Word = word,
            Key = WordFilterNormalizer.Key(word),
            Action = action,
            Match = WordFilterMatch.WholeWord,
        };

        db.WordFilterWords.Add(row);
        await db.SaveChangesAsync();

        return row.Id;
    }

    private sealed class TestDbContextFactory(DbContextOptions<VortexDbContext> options)
        : IDbContextFactory<VortexDbContext>
    {
        public VortexDbContext CreateDbContext() => new(options);

        public Task<VortexDbContext> CreateDbContextAsync(CancellationToken ct = default) =>
            Task.FromResult(CreateDbContext());
    }
}
