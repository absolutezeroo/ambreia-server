using System;
using Vortex.Primitives.Players.Enums.Wallet;
using Vortex.Primitives.Players.Wallet;
using Vortex.Primitives.RewardVault.Enums;

namespace Vortex.Primitives.RewardVault;

/// <summary>
/// Which wallet currencies the Earnings window can hold, and the reward type the client reads for
/// each. The client knows exactly two (EarningsView.onIncomeRewardDataReceived: 0 = duckets,
/// 1 = credits); every other currency is paid straight to the wallet instead.
/// </summary>
public static class RewardVaultRouting
{
    private const int DucketsActivityPointType = 0;

    public static bool TryGetRewardType(CurrencyKind currency, out RewardVaultType type)
    {
        if (currency.CurrencyType == CurrencyType.Credits)
        {
            type = RewardVaultType.Credits;
            return true;
        }

        if (
            currency.CurrencyType == CurrencyType.ActivityPoints
            && currency.ActivityPointType == DucketsActivityPointType
        )
        {
            type = RewardVaultType.Duckets;
            return true;
        }

        type = default;
        return false;
    }

    public static CurrencyKind ToCurrencyKind(RewardVaultType type) =>
        type switch
        {
            RewardVaultType.Credits => new CurrencyKind { CurrencyType = CurrencyType.Credits },
            RewardVaultType.Duckets => new CurrencyKind
            {
                CurrencyType = CurrencyType.ActivityPoints,
                ActivityPointType = DucketsActivityPointType,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Not a currency."),
        };
}
