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

    /// <summary>
    /// Builders Club membership time. <c>ExtraParam</c> is the number of days — the product the
    /// client's productdata names <c>builders_club_14_days</c>, <c>builders_club_1_month</c>, ...
    /// </summary>
    /// <remarks>
    /// The client has no product type for it (<c>PRODUCT_TYPE_*</c> stops at habbicon), and the
    /// official wire code is unknown. Its own code reads off the wire like any non-badge product,
    /// and <c>Product.as</c> only logs that it cannot draw an icon for it.
    /// </remarks>
    BuildersClubDays = 8,

    /// <summary>
    /// A Builders Club furni-limit add-on. <c>ExtraParam</c> is the number of furni — the
    /// <c>builders_club_addon_45furnis</c> family.
    /// </summary>
    BuildersClubFurni = 9,
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
            ProductType.BuildersClubDays => "builders_club_days",
            ProductType.BuildersClubFurni => "builders_club_furni",
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
            "builders_club_days" => ProductType.BuildersClubDays,
            "builders_club_furni" => ProductType.BuildersClubFurni,
            _ => throw new ArgumentOutOfRangeException(nameof(productType), productType, null),
        };
}
