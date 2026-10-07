using System;

namespace Vortex.Primitives.Furniture.Enums;

public enum ProductType
{
    Floor = 0,
    Wall = 1,
    Effect = 2,
    Badge = 3,
    Robot = 4,
    Premium = 5,
    Pet = 6,

    // 7 was the chat bubble ("chat_style"), removed in M0 bis. 8 and 9 were the Builders Membership days
    // and furni add-ons, removed in M0. Not renumbered: the values are persisted.
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
            ProductType.Premium => "h",
            ProductType.Pet => "p",
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
            "h" => ProductType.Premium,
            "p" => ProductType.Pet,
            _ => throw new ArgumentOutOfRangeException(nameof(productType), productType, null),
        };
}
