using FluentAssertions;
using Vortex.Primitives.Players.Enums.Wallet;
using Vortex.Primitives.RewardVault;
using Vortex.Primitives.RewardVault.Enums;
using Vortex.Progression.Achievements;
using Xunit;

namespace Vortex.Players.Tests.Achievements;

public sealed class AchievementEarningTests
{
    private static AchievementLevelUp LevelUp(int level, int rewardType, int amount) =>
        new()
        {
            Level = level,
            BadgeCode = "ACH_RoomEntry2",
            RemovedBadgeCode = "ACH_RoomEntry1",
            RewardAmount = amount,
            RewardType = rewardType,
            ScorePoints = 10,
        };

    [Fact]
    public void ALevelUp_IsAnAchievementsEarning_KeyedByAchievementAndLevel()
    {
        IncomeEarning earning = AchievementEarning.For(
            12,
            LevelUp(level: 2, rewardType: 0, amount: 50)
        );

        earning.Category.Should().Be(RewardVaultCategoryType.Achievements);
        earning.Amount.Should().Be(50);
        earning.SourceKey.Should().Be("achievement:12:level:2");
        earning.Currency.CurrencyType.Should().Be(CurrencyType.ActivityPoints);
        earning.Currency.ActivityPointType.Should().Be(0);
    }

    [Fact]
    public void ACreditsReward_KeepsItsCurrency()
    {
        AchievementEarning
            .For(12, LevelUp(2, rewardType: -1, amount: 5))
            .Currency.CurrencyType.Should()
            .Be(CurrencyType.Credits);
    }
}
