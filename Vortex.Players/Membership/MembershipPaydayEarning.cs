using System;
using System.Globalization;
using Vortex.Primitives.Players.Enums.Wallet;
using Vortex.Primitives.Players.Wallet;
using Vortex.Primitives.RewardVault;
using Vortex.Primitives.RewardVault.Enums;

namespace Vortex.Players.Membership;

/// <summary>
/// One Membership payday, as an Earnings entry. Keyed by the payday's date: the payday loop can be
/// re-run after a failed save, and the same date must not pay twice.
/// </summary>
public static class MembershipPaydayEarning
{
    public static IncomeEarning For(DateTime payday, int credits) =>
        new()
        {
            Category = RewardVaultCategoryType.Premium,
            Currency = new CurrencyKind { CurrencyType = CurrencyType.Credits },
            Amount = credits,
            SourceKey = string.Create(
                CultureInfo.InvariantCulture,
                $"club-payday:{payday:yyyy-MM-dd}"
            ),
        };
}
