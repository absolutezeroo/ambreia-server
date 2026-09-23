using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Vortex.Dashboard.API.Admin.Hotel;
using Vortex.Database.Context;
using Vortex.Database.Entities.Moderation;
using Vortex.Primitives.Moderation.WordFilter;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Dashboard.Tests;

/// <summary>
/// Staff's edits to the hotel-wide word list. What matters beyond the rows is the reload: the hotel
/// filters with a compiled copy, so a write that does not reload it has changed nothing anyone sees.
/// </summary>
public sealed class WordFilterAdminServiceTests
{
    private readonly DbContextOptions<VortexDbContext> _options =
        new DbContextOptionsBuilder<VortexDbContext>()
            .UseInMemoryDatabase($"wordfilter-admin-{Guid.NewGuid():N}")
            .Options;

    private int _reloads;

    [Fact]
    public async Task Add_StoresTheWordAndItsKey_AndReloads()
    {
        (
            await Service()
                .AddAsync(
                    "C.0.N",
                    WordFilterAction.Refuse,
                    WordFilterMatch.WholeWord,
                    null,
                    "ops@x",
                    default
                )
        )
            .Should()
            .Be(WordFilterAdminOutcome.Done);

        await using VortexDbContext db = new(_options);
        WordFilterWordEntity row = await db.WordFilterWords.SingleAsync();

        row.Word.Should().Be("C.0.N");
        row.Key.Should().Be("con");
        row.Action.Should().Be(WordFilterAction.Refuse);
        row.CreatedBy.Should().Be("ops@x");
        _reloads.Should().Be(1);
    }

    [Fact]
    public async Task Add_RefusesASecondSpellingOfTheSameWord()
    {
        WordFilterAdminService service = Service();
        await service.AddAsync(
            "con",
            WordFilterAction.Replace,
            WordFilterMatch.WholeWord,
            null,
            "ops@x",
            default
        );

        (
            await service.AddAsync(
                "C O N",
                WordFilterAction.Refuse,
                WordFilterMatch.Partial,
                null,
                "ops@x",
                default
            )
        )
            .Should()
            .Be(WordFilterAdminOutcome.Duplicate);

        await using VortexDbContext db = new(_options);
        (await db.WordFilterWords.CountAsync()).Should().Be(1);
        _reloads.Should().Be(1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...")]
    public async Task Add_RefusesAWordWithNoLetters(string word)
    {
        (
            await Service()
                .AddAsync(
                    word,
                    WordFilterAction.Replace,
                    WordFilterMatch.WholeWord,
                    null,
                    "ops@x",
                    default
                )
        )
            .Should()
            .Be(WordFilterAdminOutcome.Invalid);

        _reloads.Should().Be(0);
    }

    [Fact]
    public async Task Add_RefusesAWordOrReplacementPastTheColumn()
    {
        WordFilterAdminService service = Service();

        (
            await service.AddAsync(
                new string('a', 65),
                WordFilterAction.Replace,
                WordFilterMatch.WholeWord,
                null,
                "ops@x",
                default
            )
        )
            .Should()
            .Be(WordFilterAdminOutcome.Invalid);
        (
            await service.AddAsync(
                "con",
                WordFilterAction.Replace,
                WordFilterMatch.WholeWord,
                new string('*', 65),
                "ops@x",
                default
            )
        )
            .Should()
            .Be(WordFilterAdminOutcome.Invalid);

        _reloads.Should().Be(0);
    }

    [Fact]
    public async Task Add_StoresABlankReplacementAsTheDefault()
    {
        await Service()
            .AddAsync(
                "con",
                WordFilterAction.Replace,
                WordFilterMatch.WholeWord,
                "  ",
                "ops@x",
                default
            );

        await using VortexDbContext db = new(_options);
        (await db.WordFilterWords.SingleAsync()).Replacement.Should().BeNull();
    }

    [Fact]
    public async Task Add_RevivesASoftDeletedWord()
    {
        WordFilterAdminService service = Service();
        await service.AddAsync(
            "con",
            WordFilterAction.Replace,
            WordFilterMatch.WholeWord,
            null,
            "ops@x",
            default
        );
        int id = await IdOfAsync("con");
        await service.RemoveAsync(id, default);

        (
            await service.AddAsync(
                "con",
                WordFilterAction.Refuse,
                WordFilterMatch.Partial,
                null,
                "other@x",
                default
            )
        )
            .Should()
            .Be(WordFilterAdminOutcome.Done);

        await using VortexDbContext db = new(_options);
        WordFilterWordEntity row = await db.WordFilterWords.IgnoreQueryFilters().SingleAsync();
        row.Id.Should().Be(id);
        row.DeletedAt.Should().BeNull();
        row.Action.Should().Be(WordFilterAction.Refuse);
        row.CreatedBy.Should().Be("other@x");
    }

    [Fact]
    public async Task Update_ChangesTheWord_AndReloads()
    {
        WordFilterAdminService service = Service();
        await service.AddAsync(
            "con",
            WordFilterAction.Replace,
            WordFilterMatch.WholeWord,
            null,
            "ops@x",
            default
        );
        int id = await IdOfAsync("con");

        (
            await service.UpdateAsync(
                id,
                "conne",
                WordFilterAction.Refuse,
                WordFilterMatch.Partial,
                "***",
                default
            )
        )
            .Should()
            .Be(WordFilterAdminOutcome.Done);

        await using VortexDbContext db = new(_options);
        WordFilterWordEntity row = await db.WordFilterWords.SingleAsync();
        row.Word.Should().Be("conne");
        row.Key.Should().Be("conne");
        row.Action.Should().Be(WordFilterAction.Refuse);
        row.Match.Should().Be(WordFilterMatch.Partial);
        row.Replacement.Should().Be("***");
        _reloads.Should().Be(2);
    }

    [Fact]
    public async Task Update_ToAnotherListedWord_IsADuplicate()
    {
        WordFilterAdminService service = Service();
        await service.AddAsync(
            "con",
            WordFilterAction.Replace,
            WordFilterMatch.WholeWord,
            null,
            "ops@x",
            default
        );
        await service.AddAsync(
            "nul",
            WordFilterAction.Replace,
            WordFilterMatch.WholeWord,
            null,
            "ops@x",
            default
        );

        (
            await service.UpdateAsync(
                await IdOfAsync("nul"),
                "C0N",
                WordFilterAction.Replace,
                WordFilterMatch.WholeWord,
                null,
                default
            )
        )
            .Should()
            .Be(WordFilterAdminOutcome.Duplicate);
    }

    [Fact]
    public async Task Update_KeepingItsOwnKey_IsNotADuplicateOfItself()
    {
        WordFilterAdminService service = Service();
        await service.AddAsync(
            "con",
            WordFilterAction.Replace,
            WordFilterMatch.WholeWord,
            null,
            "ops@x",
            default
        );

        (
            await service.UpdateAsync(
                await IdOfAsync("con"),
                "CON",
                WordFilterAction.Refuse,
                WordFilterMatch.WholeWord,
                null,
                default
            )
        )
            .Should()
            .Be(WordFilterAdminOutcome.Done);
    }

    /// <summary>
    /// The unique index on the key covers soft-deleted rows too — MySQL has no filtered index — so
    /// taking a dead word's key would be a unique violation. The dead row gives its key up and keeps
    /// its text, which the journal still shows.
    /// </summary>
    [Fact]
    public async Task Update_ToADeletedWordsKey_FreesThatKey()
    {
        WordFilterAdminService service = Service();
        await service.AddAsync(
            "con",
            WordFilterAction.Replace,
            WordFilterMatch.WholeWord,
            null,
            "ops@x",
            default
        );
        int deadId = await IdOfAsync("con");
        await service.RemoveAsync(deadId, default);
        await service.AddAsync(
            "nul",
            WordFilterAction.Replace,
            WordFilterMatch.WholeWord,
            null,
            "ops@x",
            default
        );

        (
            await service.UpdateAsync(
                await IdOfAsync("nul"),
                "con",
                WordFilterAction.Replace,
                WordFilterMatch.WholeWord,
                null,
                default
            )
        )
            .Should()
            .Be(WordFilterAdminOutcome.Done);

        await using VortexDbContext db = new(_options);
        WordFilterWordEntity dead = await db
            .WordFilterWords.IgnoreQueryFilters()
            .SingleAsync(w => w.Id == deadId);
        dead.Key.Should().NotBe("con");
        dead.Word.Should().Be("con");
        (await db.WordFilterWords.IgnoreQueryFilters().CountAsync(w => w.Key == "con"))
            .Should()
            .Be(1);
    }

    [Fact]
    public async Task Remove_SoftDeletes_AndReloads()
    {
        WordFilterAdminService service = Service();
        await service.AddAsync(
            "con",
            WordFilterAction.Replace,
            WordFilterMatch.WholeWord,
            null,
            "ops@x",
            default
        );

        (await service.RemoveAsync(await IdOfAsync("con"), default))
            .Should()
            .Be(WordFilterAdminOutcome.Done);

        await using VortexDbContext db = new(_options);
        (await db.WordFilterWords.CountAsync()).Should().Be(0);
        (await db.WordFilterWords.IgnoreQueryFilters().SingleAsync())
            .DeletedAt.Should()
            .NotBeNull();
        _reloads.Should().Be(2);
    }

    [Fact]
    public async Task Remove_OfAMissingWord_IsNotFound_AndDoesNotReload()
    {
        (await Service().RemoveAsync(404, default)).Should().Be(WordFilterAdminOutcome.NotFound);

        _reloads.Should().Be(0);
    }

    private WordFilterAdminService Service() =>
        new(
            new TestDbContextFactory(_options),
            FakeProxy.Create<IWordFilter>(call =>
            {
                if (call.Method.Name == nameof(IWordFilter.ReloadAsync))
                {
                    _reloads++;
                }

                return null;
            })
        );

    private async Task<int> IdOfAsync(string key)
    {
        await using VortexDbContext db = new(_options);

        return (
            await db.WordFilterWords.IgnoreQueryFilters().Where(w => w.Key == key).SingleAsync()
        ).Id;
    }

    private sealed class TestDbContextFactory(DbContextOptions<VortexDbContext> options)
        : IDbContextFactory<VortexDbContext>
    {
        public VortexDbContext CreateDbContext() => new(options);

        public Task<VortexDbContext> CreateDbContextAsync(CancellationToken ct = default) =>
            Task.FromResult(CreateDbContext());
    }
}
