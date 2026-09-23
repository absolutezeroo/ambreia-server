using FluentAssertions;
using Vortex.Primitives.Moderation.WordFilter;
using Xunit;

namespace Vortex.Hosting.Tests.WordFilter;

/// <summary>
/// The fold both sides of the hotel-wide filter go through. One output character per input
/// character is the property everything else leans on: it is what lets a replacement swap exactly
/// the offending span of the original text.
/// </summary>
public sealed class WordFilterNormalizerTests
{
    [Theory]
    [InlineData("CON", "con")]
    [InlineData("Çôñ", "con")]
    [InlineData("c0n", "con")]
    [InlineData("c.o.n", "c o n")]
    [InlineData("4@3105$7", "aaeiosst")]
    // `!` is not leet for `i`: "con!" must keep its word boundary.
    [InlineData("con!", "con ")]
    [InlineData("a😀b", "a  b")]
    public void Normalize_FoldsCaseAccentsLeetAndSeparators(string input, string expected)
    {
        WordFilterNormalizer.Normalize(input).Should().Be(expected);
    }

    [Fact]
    public void Normalize_KeepsTheLengthOfTheInput()
    {
        const string input = "Ça va, c.o.n ?! 😀";

        WordFilterNormalizer.Normalize(input).Length.Should().Be(input.Length);
    }

    [Theory]
    [InlineData("Coonnn", "con")]
    [InlineData("c o n", "con")]
    [InlineData("C.0.N", "con")]
    public void Key_IsTheSameForEverySpellingOfAWord(string word, string expected)
    {
        WordFilterNormalizer.Key(word).Should().Be(expected);
    }
}
