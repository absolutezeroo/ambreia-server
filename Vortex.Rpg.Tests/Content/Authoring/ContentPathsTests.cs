using FluentAssertions;
using Vortex.Rpg.Content.Authoring;
using Xunit;

namespace Vortex.Rpg.Tests.Content.Authoring;

/// <summary>Content authoring §3, §5: where a document is written, and which paths are refused.</summary>
public sealed class ContentPathsTests
{
    [Theory]
    [InlineData("item", "item.weapon.rusted_sword", "item/rusted_sword.json")]
    [InlineData("class", "class.mage", "class/mage.json")]
    [InlineData("quest", "quest.camp.wolves", "quest/wolves.json")]
    public void ADocumentMadeInTheEditor_TakesItsKindAndLastIdSegment(
        string kind,
        string id,
        string path
    ) => ContentPaths.Derive(kind, id).Should().Be(path);

    [Theory]
    [InlineData("item/rusted_sword.json")]
    [InlineData("item/weapons/rusted_sword.json")]
    public void APathUnderItsKindFolder_IsSafe(string path) =>
        ContentPaths.IsSafe("item", path).Should().BeTrue();

    [Theory]
    [InlineData("item/../../x.json")]
    [InlineData("../item/x.json")]
    [InlineData("/item/x.json")]
    [InlineData("C:/item/x.json")]
    [InlineData(@"item\x.json")]
    [InlineData("monster/x.json")]
    [InlineData("item/x.txt")]
    [InlineData("item/X.json")]
    [InlineData("item/.json")]
    [InlineData("")]
    public void AnyOtherPath_IsRefused(string path) =>
        ContentPaths.IsSafe("item", path).Should().BeFalse();

    [Fact]
    public void ALongPath_IsRefused() =>
        ContentPaths.IsSafe("item", "item/" + new string('a', 252) + ".json").Should().BeFalse();
}
