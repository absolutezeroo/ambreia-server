using System.Collections.Generic;

namespace Vortex.Primitives.Catalog;

public enum CatalogPageLayout
{
    Default3x3,
    BadgeDisplay,
    ClubBuy,
    ClubGifts,
    Frontpage4,
    FrontpageFeatured,
    GuildCustomFurni,
    GuildForum,
    GuildFrontpage,
    InfoDuckets,
    InfoLoyalty,
    InfoRentables,
    LoyaltyVipBuy,
    Marketplace,
    MarketplaceOwnItems,
    Monkey,
    PetCustomization,
    Pets,
    Pets2,
    Pets3,
    Recycler,
    RecyclerInfo,
    RecyclerPrizes,
    RoomAds,
    SingleBundle,
    SoundMachine,
    SpacesNew,
    Trophies,
    VipBuy,
    OldMarketplace,
    OldMarketplaceOwnItems,
}

public static class CatalogPageLayoutExtensions
{
    private static readonly Dictionary<CatalogPageLayout, string> _toWire = new()
    {
        [CatalogPageLayout.Default3x3] = "default_3x3",
        [CatalogPageLayout.BadgeDisplay] = "badge_display",
        [CatalogPageLayout.ClubBuy] = "club_buy",
        [CatalogPageLayout.ClubGifts] = "club_gifts",
        [CatalogPageLayout.Frontpage4] = "frontpage4",
        [CatalogPageLayout.FrontpageFeatured] = "frontpage_featured",
        [CatalogPageLayout.GuildCustomFurni] = "guild_custom_furni",
        [CatalogPageLayout.GuildForum] = "guild_forum",
        [CatalogPageLayout.GuildFrontpage] = "guild_frontpage",
        [CatalogPageLayout.InfoDuckets] = "info_duckets",
        [CatalogPageLayout.InfoLoyalty] = "info_loyalty",
        [CatalogPageLayout.InfoRentables] = "info_rentables",
        [CatalogPageLayout.LoyaltyVipBuy] = "loyalty_vip_buy",
        [CatalogPageLayout.Marketplace] = "marketplace",
        [CatalogPageLayout.MarketplaceOwnItems] = "marketplace_own_items",
        [CatalogPageLayout.Monkey] = "monkey",
        [CatalogPageLayout.PetCustomization] = "petcustomization",
        [CatalogPageLayout.Pets] = "pets",
        [CatalogPageLayout.Pets2] = "pets2",
        [CatalogPageLayout.Pets3] = "pets3",
        [CatalogPageLayout.Recycler] = "recycler",
        [CatalogPageLayout.RecyclerInfo] = "recycler_info",
        [CatalogPageLayout.RecyclerPrizes] = "recycler_prizes",
        [CatalogPageLayout.RoomAds] = "roomads",
        [CatalogPageLayout.SingleBundle] = "single_bundle",
        [CatalogPageLayout.SoundMachine] = "soundmachine",
        [CatalogPageLayout.SpacesNew] = "spaces_new",
        [CatalogPageLayout.Trophies] = "trophies",
        [CatalogPageLayout.VipBuy] = "vip_buy",
        [CatalogPageLayout.OldMarketplace] = "old_layout_marketplace",
        [CatalogPageLayout.OldMarketplaceOwnItems] = "old_layout_marketplace_own_items",
    };

    private static readonly Dictionary<string, CatalogPageLayout> _fromWire;

    static CatalogPageLayoutExtensions()
    {
        _fromWire = new Dictionary<string, CatalogPageLayout>(_toWire.Count);
        foreach (KeyValuePair<CatalogPageLayout, string> kv in _toWire)
        {
            _fromWire[kv.Value] = kv.Key;
        }
    }

    public static string ToLayoutString(this CatalogPageLayout layout)
    {
        return _toWire.TryGetValue(layout, out string? s) ? s : "default_3x3";
    }

    public static CatalogPageLayout FromLayoutString(string value)
    {
        return _fromWire.TryGetValue(value, out CatalogPageLayout layout)
            ? layout
            : CatalogPageLayout.Default3x3;
    }
}
