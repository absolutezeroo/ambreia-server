using System;
using FluentAssertions;
using Vortex.Permissions.WordFilter;
using Vortex.Primitives.Moderation.WordFilter;
using Xunit;

namespace Vortex.Hosting.Tests.WordFilter;

/// <summary>
/// The compiled word list: what it catches, what it must leave alone, and what it replaces.
/// </summary>
public sealed class WordFilterSetTests
{
    private static WordFilterSet Set(params WordFilterEntry[] entries) =>
        WordFilterSet.Build(entries, "bobba", TimeSpan.FromMilliseconds(100));

    private static WordFilterEntry Replace(
        string word,
        WordFilterMatch match = WordFilterMatch.WholeWord,
        string? with = null
    ) => new(1, word, WordFilterAction.Replace, match, with);

    private static WordFilterEntry Refuse(string word, int id = 2) =>
        new(id, word, WordFilterAction.Refuse, WordFilterMatch.WholeWord, null);

    [Theory]
    [InlineData("t'es con")]
    [InlineData("t'es CON")]
    [InlineData("t'es cooonnn")]
    [InlineData("t'es c.o.n")]
    [InlineData("t'es c o n")]
    [InlineData("t'es c0n")]
    [InlineData("t'es çôn")]
    public void EveryDisguise_IsCaught(string text)
    {
        Set(Replace("con")).Evaluate(text).Changed.Should().BeTrue();
    }

    [Theory]
    [InlineData("un concert", "con")]
    [InlineData("Scunthorpe", "cunt")]
    [InlineData("une classe", "ass")]
    public void WholeWord_DoesNotMatchInsideALongerWord(string text, string word)
    {
        WordFilterVerdict verdict = Set(Replace(word)).Evaluate(text);

        verdict.Changed.Should().BeFalse();
        verdict.Text.Should().BeSameAs(text);
    }

    [Fact]
    public void ADoubledLetter_IsRequired_SoAssDoesNotBanAs()
    {
        WordFilterSet set = Set(Replace("ass"));

        set.Evaluate("un as du volant").Changed.Should().BeFalse();
        set.Evaluate("t'es un ass").Changed.Should().BeTrue();
        set.Evaluate("t'es un asssss").Changed.Should().BeTrue();
    }

    /// <summary>
    /// Partial means "inside a word", not "across two": the letters of "sex" sit in "les ex" with a
    /// space between them, and ordinary French must not be mangled for it.
    /// </summary>
    [Theory]
    [InlineData("tu connais les ex", "sex")]
    [InlineData("je suis pas sûr", "ass")]
    public void Partial_DoesNotReachAcrossTwoWords(string text, string word)
    {
        WordFilterVerdict verdict = Set(Replace(word, WordFilterMatch.Partial)).Evaluate(text);

        verdict.Changed.Should().BeFalse();
        verdict.Text.Should().BeSameAs(text);
    }

    [Theory]
    [InlineData("t'es c o n", "t'es bobba")]
    [InlineData("c.o.n", "bobba")]
    [InlineData("unconcert", "unbobbacert")]
    public void Partial_StillCatchesASpelledOutWord_AndOneInsideAnother(
        string text,
        string expected
    )
    {
        Set(Replace("con", WordFilterMatch.Partial)).Evaluate(text).Text.Should().Be(expected);
    }

    [Fact]
    public void Partial_MatchesInsideALongerWord()
    {
        Set(Replace("con", WordFilterMatch.Partial))
            .Evaluate("un concert")
            .Text.Should()
            .Be("un bobbacert");
    }

    [Theory]
    [InlineData("con!", "bobba!")]
    [InlineData("(con)", "(bobba)")]
    [InlineData("Salut, con, ça va", "Salut, bobba, ça va")]
    [InlineData("c.o.n.", "bobba.")]
    public void Replace_KeepsSurroundingPunctuation(string text, string expected)
    {
        Set(Replace("con")).Evaluate(text).Text.Should().Be(expected);
    }

    [Fact]
    public void Replace_UsesTheWordsOwnReplacementWhenItHasOne()
    {
        Set(Replace("con", with: "***")).Evaluate("con").Text.Should().Be("***");
    }

    [Fact]
    public void Replace_SwapsEveryOccurrence()
    {
        Set(Replace("con")).Evaluate("con et con").Text.Should().Be("bobba et bobba");
    }

    [Fact]
    public void ARefuseWord_RefusesTheWholeText_EvenBesideReplaceWords()
    {
        WordFilterVerdict verdict = Set(Replace("con"), Refuse("nazi", id: 7))
            .Evaluate("con de nazi");

        verdict.Refused.Should().BeTrue();
        verdict.FirstWordId.Should().Be(7);
        verdict.FirstAction.Should().Be(WordFilterAction.Refuse);
    }

    [Fact]
    public void NoMatch_ReturnsTheSameInstance()
    {
        const string text = "bonjour tout le monde";

        WordFilterVerdict verdict = Set(Replace("con")).Evaluate(text);

        verdict.Text.Should().BeSameAs(text);
        verdict.Changed.Should().BeFalse();
        verdict.Refused.Should().BeFalse();
        verdict.FirstWordId.Should().Be(0);
    }

    /// <summary>
    /// Any player can send a 60 000-character chat line, and it must be filtered inside the timeout
    /// rather than failing open. A regression pin: today's backtracking engine also passes it (it
    /// makes <c>a+</c> atomic), so this does not by itself prove the engine choice — the
    /// NonBacktracking option does that by construction.
    /// </summary>
    [Fact]
    public void ALongAdversarialLine_IsFilteredWithinTheTimeout()
    {
        WordFilterSet set = WordFilterSet.Build(
            [new WordFilterEntry(1, "ab", WordFilterAction.Refuse, WordFilterMatch.Partial, null)],
            "bobba",
            TimeSpan.FromMilliseconds(100)
        );

        // The trailing "cb" matters: with no 'b' anywhere, the engine rejects the line on a single
        // scan for the required letter. With one after a blocker, every start position runs a+ to
        // the 'c', fails on it, and backtracks.
        Action evaluate = () => set.Evaluate(new string('a', 60_000) + "cb");

        evaluate.Should().NotThrow();
    }

    [Fact]
    public void TheEmptySet_PassesEverything()
    {
        WordFilterSet.Empty.Evaluate("con").Text.Should().Be("con");
    }

    [Fact]
    public void AMultiWordEntry_MatchesAcrossTheSpace()
    {
        Set(Replace("sale con"))
            .Evaluate("espèce de sale con !")
            .Text.Should()
            .Be("espèce de bobba !");
    }
}
