using System;
using System.Globalization;
using Vortex.Primitives.Players.Enums.Wallet;
using Vortex.Primitives.Players.Wallet;
using Vortex.Primitives.Vault;
using Vortex.Primitives.Vault.Enums;

namespace Vortex.Players.Club;

/// <summary>
/// One Habbo Club payday, as an Earnings entry. Keyed by the payday's date: the payday loop can be
/// re-run after a failed save, and the same date must not pay twice.
/// </summary>
public static class ClubPaydayEarning
{
    public static IncomeEarning For(DateTime payday, int credits) =>
        new()
        {
            Category = VaultRewardCategoryType.HabboClub,
            Currency = new CurrencyKind { CurrencyType = CurrencyType.Credits },
            Amount = credits,
            SourceKey = string.Create(
                CultureInfo.InvariantCulture,
                $"club-payday:{payday:yyyy-MM-dd}"
            ),
        };
}
