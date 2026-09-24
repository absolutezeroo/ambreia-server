using System;
using FluentAssertions;
using Vortex.Players.Club;
using Vortex.Primitives.Players.Enums.Wallet;
using Vortex.Primitives.Vault;
using Vortex.Primitives.Vault.Enums;
using Xunit;

namespace Vortex.Players.Tests.Club;

public sealed class ClubPaydayEarningTests
{
    [Fact]
    public void APayday_IsAHabboClubCreditsEarning_KeyedByItsDate()
    {
        IncomeEarning earning = ClubPaydayEarning.For(
            new DateTime(2026, 9, 1, 13, 45, 0, DateTimeKind.Utc),
            41
        );

        earning.Category.Should().Be(VaultRewardCategoryType.HabboClub);
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
            ClubPaydayEarning
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
