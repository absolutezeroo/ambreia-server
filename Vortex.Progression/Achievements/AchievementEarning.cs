using Vortex.Primitives.Players.Wallet;
using Vortex.Primitives.Vault;
using Vortex.Primitives.Vault.Enums;

namespace Vortex.Progression.Achievements;

/// <summary>What one achievement level pays, as an Earnings entry. The key is the level itself: a
/// level is crossed once, so a replayed payout for it is the same earning.</summary>
public static class AchievementEarning
{
    public static IncomeEarning For(int achievementId, AchievementLevelUp levelUp) =>
        new()
        {
            Category = VaultRewardCategoryType.Achievements,
            Currency = CurrencyRewardRules.KindFor(levelUp.RewardType),
            Amount = levelUp.RewardAmount,
            SourceKey = $"achievement:{achievementId}:level:{levelUp.Level}",
        };
}
