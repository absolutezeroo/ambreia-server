using FluentAssertions;
using Vortex.Furniture.Providers;
using Vortex.Inventory.Fulfillment.Strategies;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Furniture.StuffData;
using Xunit;

namespace Vortex.Database.Tests.Catalog;

/// <summary>
/// A bought wallpaper, floor or landscape must carry its pattern code as legacy stuff data — the
/// client reads it back with <c>stuffData.getLegacyString()</c> to show and apply the decoration.
/// The purchase path used to plan the item with no extra data at all.
/// </summary>
public sealed class DecorationFulfillmentTests
{
    [Theory]
    [InlineData(FurnitureCategory.WallPaper, "101")]
    [InlineData(FurnitureCategory.Floor, "110")]
    [InlineData(FurnitureCategory.Landscape, "1.10")]
    public void ADecorationCarriesItsPatternCodeAsLegacyStuffData(
        FurnitureCategory category,
        string code
    )
    {
        string? blob = FurnitureFulfillmentStrategy.BuildDecorationExtraData(category, code);

        blob.Should().NotBeNull();
        new StuffDataFactory()
            .CreateStuffDataFromJson(StuffDataType.LegacyKey, blob!)
            .GetLegacyString()
            .Should()
            .Be(code);
    }

    [Theory]
    [InlineData(FurnitureCategory.Default, "101")]
    [InlineData(FurnitureCategory.WallPaper, "")]
    [InlineData(FurnitureCategory.WallPaper, null)]
    public void AnythingElseKeepsNoExtraData(FurnitureCategory category, string? code) =>
        FurnitureFulfillmentStrategy.BuildDecorationExtraData(category, code).Should().BeNull();
}
