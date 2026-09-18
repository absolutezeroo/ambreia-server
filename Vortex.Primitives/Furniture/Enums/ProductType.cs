using System;

namespace Vortex.Primitives.Furniture.Enums;

public enum ProductType
{
    Floor = 0,
    Wall = 1,
    Effect = 2,
    Badge = 3,
    Robot = 4,
    HabboClub = 5,
    Pet = 6,

    /// <summary>
    /// A chat bubble. <c>ExtraParam</c> is the id in the client's own style library.
    /// </summary>
    /// <remarks>
    /// The one legacy code that is a word rather than a letter, and it is the client's spelling:
    /// <c>PRODUCT_TYPE_CHAT_STYLE = "chat_style"</c>. Products of this type read off the wire like
    /// any non-badge product (class id, extra param, count, unique flag), so nothing in the
    /// serializer special-cases it.
    /// </remarks>
    ChatStyle = 7,
}

public static class ProductTypeEnumExtension
{
    public static string ToLegacyString(this ProductType productType) =>
        productType switch
        {
            ProductType.Floor => "s",
            ProductType.Wall => "i",
            ProductType.Effect => "e",
            ProductType.Badge => "b",
            ProductType.Robot => "r",
            ProductType.HabboClub => "h",
            ProductType.Pet => "p",
            ProductType.ChatStyle => "chat_style",
            _ => throw new ArgumentOutOfRangeException(nameof(productType), productType, null),
        };

    public static ProductType FromLegacyString(this string productType) =>
        productType switch
        {
            "s" => ProductType.Floor,
            "i" => ProductType.Wall,
            "e" => ProductType.Effect,
            "b" => ProductType.Badge,
            "r" => ProductType.Robot,
            "h" => ProductType.HabboClub,
            "p" => ProductType.Pet,
            "chat_style" => ProductType.ChatStyle,
            _ => throw new ArgumentOutOfRangeException(nameof(productType), productType, null),
        };
}
