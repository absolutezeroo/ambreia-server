using FluentAssertions;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Rooms.Grains;
using Xunit;

namespace Vortex.Rooms.Tests.Furniture;

/// <summary>
/// Which room property each decoration category writes. The keys are the strings the client's
/// onRoomProperty switches on, so a wrong one applies nothing without an error.
/// </summary>
public sealed class RoomDecorationTests
{
    [Theory]
    [InlineData(FurnitureCategory.WallPaper, RoomPropertyType.WALLPAPER)]
    [InlineData(FurnitureCategory.Floor, RoomPropertyType.FLOOR)]
    [InlineData(FurnitureCategory.Landscape, RoomPropertyType.LANDSCAPE)]
    [InlineData(FurnitureCategory.Default, null)]
    [InlineData(FurnitureCategory.Poster, null)]
    public void EachDecorationCategoryWritesItsOwnProperty(
        FurnitureCategory category,
        string? expected
    ) => RoomGrain.DecorationPropertyKey(category).Should().Be(expected);
}
