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

    // 7 was the chat bubble ("chat_style"), removed in M0 bis. 8 and 9 were the Builders Club days
    // and furni add-ons, removed in M0. Not renumbered: the values are persisted.
}

public static class ProductTypeEnumExtension
{
    /// <summary>
    /// Whether something still grants this type: not a robot (player-owned bots, removed in M0 bis),
    /// nor any retired value. Nothing calls it since the catalogue left with the cut of 2026-10-07.
    /// </summary>
    public static bool IsSold(this ProductType productType) =>
        productType
            is ProductType.Floor
                or ProductType.Wall
                or ProductType.Effect
                or ProductType.Badge
                or ProductType.Premium
                or ProductType.Pet;

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
