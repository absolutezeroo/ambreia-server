using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Vortex.Observability.WordFilter;
using Vortex.Primitives.Moderation.WordFilter;
using Xunit;
using WordFilterService = Vortex.Observability.WordFilter.WordFilter;

namespace Vortex.Hosting.Tests.WordFilter;

/// <summary>
/// The service around the compiled set: loading, journaling, the name rule, and failing open.
/// </summary>
public sealed class WordFilterTests
{
    private readonly List<WordFilterHit> _journal = [];

    private WordFilterService NewFilter(params WordFilterEntry[] words) =>
        new(
            _ =>
                Task.FromResult<(IReadOnlyList<WordFilterEntry>, string, string)>(
                    (words, "bobba", "Interdit.")
                ),
            _journal.Add,
            NullLogger<WordFilterService>.Instance
        );

    private static WordFilterEntry Word(string word, WordFilterAction action, int id = 3) =>
        new(id, word, action, WordFilterMatch.WholeWord, null);

    [Fact]
    public void BeforeTheFirstReload_EverythingPasses()
    {
        using WordFilterService filter = NewFilter(Word("con", WordFilterAction.Refuse));

        WordFilterResult result = filter.Apply("con", WordFilterSource.RoomChat, 9);

        result.Refused.Should().BeFalse();
        result.Text.Should().Be("con");
        _journal.Should().BeEmpty();
    }

    [Fact]
    public async Task Apply_ReplacesAndJournals()
    {
        using WordFilterService filter = NewFilter(Word("con", WordFilterAction.Replace));
        await filter.ReloadAsync(CancellationToken.None);

        WordFilterResult result = filter.Apply("t'es con", WordFilterSource.RoomChat, 9);

        result.Text.Should().Be("t'es bobba");
        result.Changed.Should().BeTrue();
        _journal
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(
                new WordFilterHit(
                    9,
                    WordFilterSource.RoomChat,
                    3,
                    WordFilterAction.Replace,
                    "t'es con"
                )
            );
    }

    [Fact]
    public async Task Apply_RefusesAndJournals()
    {
        using WordFilterService filter = NewFilter(Word("con", WordFilterAction.Refuse));
        await filter.ReloadAsync(CancellationToken.None);

        filter.Apply("t'es con", WordFilterSource.Messenger, 9).Refused.Should().BeTrue();
        _journal.Should().ContainSingle().Which.Action.Should().Be(WordFilterAction.Refuse);
    }

    [Fact]
    public async Task ACleanText_IsNotJournaled()
    {
        using WordFilterService filter = NewFilter(Word("con", WordFilterAction.Refuse));
        await filter.ReloadAsync(CancellationToken.None);

        filter.Apply("bonjour", WordFilterSource.RoomChat, 9).Hit.Should().BeFalse();
        _journal.Should().BeEmpty();
    }

    [Fact]
    public async Task AUsername_IsRefusedByAReplaceWordToo()
    {
        using WordFilterService filter = NewFilter(Word("con", WordFilterAction.Replace));
        await filter.ReloadAsync(CancellationToken.None);

        WordFilterResult result = filter.Apply("con", WordFilterSource.Username, 9);

        result.Refused.Should().BeTrue();
        result.Text.Should().Be("con");
        _journal.Should().ContainSingle().Which.Action.Should().Be(WordFilterAction.Refuse);
    }

    [Fact]
    public async Task Test_NeverJournals()
    {
        using WordFilterService filter = NewFilter(Word("con", WordFilterAction.Refuse));
        await filter.ReloadAsync(CancellationToken.None);

        filter.Test("con").Refused.Should().BeTrue();
        _journal.Should().BeEmpty();
    }

    [Fact]
    public async Task TheJournal_KeepsAtMost512Characters()
    {
        using WordFilterService filter = NewFilter(Word("con", WordFilterAction.Replace));
        await filter.ReloadAsync(CancellationToken.None);

        filter.Apply("con " + new string('x', 1000), WordFilterSource.FeedPost, 9);

        _journal.Should().ContainSingle().Which.OriginalText.Length.Should().Be(512);
    }

    [Fact]
    public async Task RefusedMessage_ComesFromTheReload()
    {
        using WordFilterService filter = NewFilter();
        filter.RefusedMessage.Should().Be(WordFilterConfig.RefusedMessageDefault);

        await filter.ReloadAsync(CancellationToken.None);

        filter.RefusedMessage.Should().Be("Interdit.");
    }

    [Fact]
    public async Task AReload_SwapsTheList()
    {
        WordFilterEntry[] words = [Word("con", WordFilterAction.Refuse)];
        using WordFilterService filter = new(
            _ =>
                Task.FromResult<(IReadOnlyList<WordFilterEntry>, string, string)>(
                    (words, "bobba", "x")
                ),
            _journal.Add,
            NullLogger<WordFilterService>.Instance
        );
        await filter.ReloadAsync(CancellationToken.None);
        filter.Test("con").Refused.Should().BeTrue();

        words = [];
        await filter.ReloadAsync(CancellationToken.None);

        filter.Test("con").Refused.Should().BeFalse();
    }

    /// <summary>
    /// Two moderators save at once. The first reload reads the list before the second one's write
    /// and is slow; the second reads after it and is quick. Whatever the timing, the list the hotel
    /// ends up filtering with is the later read — never the earlier one landing last.
    /// </summary>
    [Fact]
    public async Task ConcurrentReloads_NeverLeaveTheEarlierReadInstalled()
    {
        using SemaphoreSlim releaseFirst = new(0, 1);
        int calls = 0;

        using WordFilterService filter = new(
            async _ =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    await releaseFirst.WaitAsync();

                    return ([], "bobba", "x");
                }

                return ([Word("con", WordFilterAction.Refuse)], "bobba", "x");
            },
            _journal.Add,
            NullLogger<WordFilterService>.Instance
        );

        Task first = filter.ReloadAsync(CancellationToken.None);
        Task second = filter.ReloadAsync(CancellationToken.None);

        releaseFirst.Release();
        await Task.WhenAll(first, second);

        filter.Test("con").Refused.Should().BeTrue();
    }

    [Fact]
    public async Task ARegexTimeout_FailsOpen()
    {
        using WordFilterService filter = new(
            _ =>
                Task.FromResult<(IReadOnlyList<WordFilterEntry>, string, string)>(
                    (
                        [
                            new WordFilterEntry(
                                1,
                                // A one-tick timeout against a 200 000-character line: even the
                                // linear engine cannot scan that inside it, so the timeout fires.
                                "ab",
                                WordFilterAction.Refuse,
                                WordFilterMatch.Partial,
                                null
                            ),
                        ],
                        "bobba",
                        "x"
                    )
                ),
            _journal.Add,
            NullLogger<WordFilterService>.Instance,
            matchTimeout: TimeSpan.FromTicks(1)
        );
        await filter.ReloadAsync(CancellationToken.None);

        WordFilterResult result = filter.Apply(
            new string('a', 200_000),
            WordFilterSource.RoomChat,
            9
        );

        result.Refused.Should().BeFalse();
        result.Changed.Should().BeFalse();
        _journal.Should().BeEmpty();
    }
}
