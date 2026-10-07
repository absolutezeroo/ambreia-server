using Vortex.Primitives.RewardVault.Enums;

namespace Vortex.Players.RewardVault;

/// <summary>One unclaimed Earnings row, as a settler is handed it.</summary>
internal sealed record RewardVaultRow(
    int Id,
    RewardVaultCategoryType Category,
    RewardVaultType RewardType,
    int Amount,
    string ProductCode
);
