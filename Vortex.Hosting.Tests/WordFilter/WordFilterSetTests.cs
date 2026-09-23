using System;
using FluentAssertions;
using Vortex.Observability.WordFilter;
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
