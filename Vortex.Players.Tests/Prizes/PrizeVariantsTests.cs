using System.Collections.Immutable;
using FluentAssertions;
using Vortex.Primitives.Prizes;
using Xunit;

namespace Vortex.Players.Tests.Prizes;

/// <summary>
/// The pool's declared variant set is what keeps a typo from quietly parking an entry in the pool
/// forever. These lock the widening rule.
/// </summary>
public sealed class PrizeVariantsTests
{
    private static readonly ImmutableArray<string> BoxColors = ["purple", "blue", "green", "red"];

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("Blue", "blue")]
    [InlineData("  RED  ", "red")]
    public void Normalize_TrimsAndLowercases(string? input, string expected)
    {
        PrizeVariants.Normalize(input).Should().Be(expected);
    }

    [Fact]
    public void ParseSet_SplitsTrimsAndDeduplicates()
    {
        PrizeVariants.ParseSet(" Blue , blue ,GREEN,, ").Should().Equal("blue", "green");
    }

    [Fact]
    public void ParseSet_EmptyMeansFreeForm()
    {
        PrizeVariants.ParseSet(null).Should().BeEmpty();
        PrizeVariants.ParseSet("  ").Should().BeEmpty();
    }

    [Fact]
    public void NormalizeForSet_KeepsAVariantTheSetDeclares()
    {
        PrizeVariants.NormalizeForSet("  BLUE ", BoxColors).Should().Be("blue");
    }

    [Fact]
    public void NormalizeForSet_WidensAnUnknownVariantToAny()
    {
        // "gold" is not a variant the pool declares, so an entry typed with it would match no draw
        // and sit undrawn forever. Widening to "any" keeps a typo visible as "drops too often"
        // rather than invisible as "never".
        PrizeVariants.NormalizeForSet("gold", BoxColors).Should().BeEmpty();
    }

    [Fact]
    public void NormalizeForSet_LeavesFreeFormPoolsAlone()
    {
        PrizeVariants.NormalizeForSet("anything", []).Should().Be("anything");
    }
}
