using System;
using FluentAssertions;
using Vortex.Collectibles.Configuration;
using Vortex.Collectibles.Vault;
using Vortex.Primitives.Server;
using Vortex.Primitives.Vault.Enums;
using Xunit;

namespace Vortex.Players.Tests.Vault;

/// <summary>
/// The switch that decides whether a category waits in the Earnings window is an operator setting.
/// It must exist in the dashboard catalogue with the default the code falls back to, and a source
/// wired without one must fail here rather than silently pick a behaviour.
/// </summary>
public sealed class EarningsRoutingPolicyTests
{
    [Theory]
    [InlineData(VaultRewardCategoryType.Achievements, "earnings.achievements.to_vault")]
    [InlineData(VaultRewardCategoryType.HabboClub, "earnings.habbo_club.to_vault")]
    public void LiveCategories_HaveTheirKey(VaultRewardCategoryType category, string key) =>
        EarningsRoutingPolicy.KeyFor(category).Should().Be(key);

    [Fact]
    public void ACategoryWithNoSource_HasNoSwitch()
    {
        Action act = () => EarningsRoutingPolicy.KeyFor(VaultRewardCategoryType.DailyGift);
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
            descriptor.DefaultValue.Should().Be(EarningsConfig.ToVaultDefault ? "true" : "false");
            descriptor.Group.Should().Be("Earnings");
        }
    }
}
