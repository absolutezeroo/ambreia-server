using Vortex.Primitives.Vault.Enums;

namespace Vortex.Collectibles.Vault;

/// <summary>One unclaimed Earnings row, as a settler is handed it.</summary>
internal sealed record VaultIncomeRow(
    int Id,
    VaultRewardCategoryType Category,
    VaultRewardType RewardType,
    int Amount,
    string ProductCode
);
