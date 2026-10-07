using System;
using FluentAssertions;
using Vortex.Players.Membership;
using Vortex.Primitives.Players.Enums.Wallet;
using Vortex.Primitives.RewardVault;
using Vortex.Primitives.RewardVault.Enums;
using Xunit;

namespace Vortex.Players.Tests.Membership;

public sealed class MembershipPaydayEarningTests
{
    [Fact]
    public void APayday_IsAMembershipCreditsEarning_KeyedByItsDate()
    {
        IncomeEarning earning = MembershipPaydayEarning.For(
            new DateTime(2026, 9, 1, 13, 45, 0, DateTimeKind.Utc),
            41
        );

        earning.Category.Should().Be(RewardVaultCategoryType.Premium);
        earning.Currency.CurrencyType.Should().Be(CurrencyType.Credits);
        earning.Amount.Should().Be(41);
        earning.SourceKey.Should().Be("club-payday:2026-09-01");
    }

    [Fact]
    public void TheKey_IsCultureIndependent()
    {
        System.Globalization.CultureInfo previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new("ar-SA");
            MembershipPaydayEarning
                .For(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), 1)
                .SourceKey.Should()
                .Be("club-payday:2026-09-01");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }
}
