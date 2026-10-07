using FluentAssertions;
using Vortex.Rpg.Content;
using Xunit;

namespace Vortex.Rpg.Tests.Content;

/// <summary>Stable content ids (spec Contenu §4.1): kind.segment[.segment…], [a-z0-9_], 64 max.</summary>
public sealed class ContentIdsTests
{
    [Theory]
    [InlineData("class.warrior")]
    [InlineData("item.weapon.rusted_sword")]
    [InlineData("progression.default")]
    public void AWellFormedId_IsValid(string id) => ContentIds.IsValid(id).Should().BeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("warrior")]
    [InlineData("Class.warrior")]
    [InlineData("item..sword")]
    [InlineData("item.rusted-sword")]
    [InlineData("item.sword.")]
    public void AMalformedId_IsNot(string id) => ContentIds.IsValid(id).Should().BeFalse();

    [Fact]
    public void AnIdLongerThan64_IsNot() =>
        ContentIds.IsValid("item." + new string('a', 60)).Should().BeFalse();

    [Fact]
    public void TheKind_IsTheFirstSegment() =>
        ContentIds.KindOf("item.weapon.rusted_sword").Should().Be("item");
}
