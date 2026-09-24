namespace Vortex.Primitives.Vault.Enums;

/// <summary>
/// The Earnings window's rows, numbered as the client's EarningsView sends them. <see cref="All"/>
/// is its Claim All button: one byte, 0xFF, read back signed.
/// </summary>
public enum VaultRewardCategoryType
{
    All = -1,
    Tutorial = 0,
    DailyGift = 1,
    Achievements = 2,
    Marketplace = 3,
    HabboClub = 4,
    LevelProgression = 5,
    RoomBundleSales = 6,
    BonusBag = 7,
    Donation = 8,
    Suprise = 9,
    SnowStorm = 10,
    Games = 11,
    WiredChest = 12,
    Agency = 13,
}
