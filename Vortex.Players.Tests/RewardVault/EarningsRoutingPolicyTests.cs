using System;
using FluentAssertions;
using Vortex.Players.RewardVault;
using Vortex.Primitives.RewardVault.Enums;
using Vortex.Primitives.Server;
using Xunit;

namespace Vortex.Players.Tests.RewardVault;

/// <summary>
/// The switch that decides whether a category waits in the Earnings window is an operator setting.
/// It must exist in the dashboard catalogue with the default the code falls back to, and a source
/// wired without one must fail here rather than silently pick a behaviour.
/// </summary>
public sealed class EarningsRoutingPolicyTests
{
    [Theory]
    [InlineData(RewardVaultCategoryType.Achievements, "earnings.achievements.to_vault")]
    [InlineData(RewardVaultCategoryType.Premium, "earnings.membership.to_vault")]
    public void LiveCategories_HaveTheirKey(RewardVaultCategoryType category, string key) =>
        EarningsRoutingPolicy.KeyFor(category).Should().Be(key);

    [Fact]
    public void ACategoryWithNoSource_HasNoSwitch()
    {
        Action act = () => EarningsRoutingPolicy.KeyFor(RewardVaultCategoryType.DailyGift);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EveryKey_IsInTheCatalogueWithTheSameDefault()
    {
        EarningsRoutingPolicy.AllKeys.Should().HaveCount(2).And.OnlyHaveUniqueItems();

        foreach (string key in EarningsRoutingPolicy.AllKeys)
        {
            ConfigKeyDescriptor? descriptor = ConfigKeyCatalog.Find(key);

            descriptor.Should().NotBeNull($"'{key}' must be editable from the dashboard");
            descriptor!.Kind.Should().Be(ConfigValueKind.Bool);
            descriptor
                .DefaultValue.Should()
                .Be(EarningsConfig.ToRewardVaultDefault ? "true" : "false");
            descriptor.Group.Should().Be("Earnings");
        }
    }
}
