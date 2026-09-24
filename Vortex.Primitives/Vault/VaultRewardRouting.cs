using System;
using Vortex.Primitives.Players.Enums.Wallet;
using Vortex.Primitives.Players.Wallet;
using Vortex.Primitives.Vault.Enums;

namespace Vortex.Primitives.Vault;

/// <summary>
/// Which wallet currencies the Earnings window can hold, and the reward type the client reads for
/// each. The client knows exactly two (EarningsView.onIncomeRewardDataReceived: 0 = duckets,
/// 1 = credits); every other currency is paid straight to the wallet instead.
/// </summary>
public static class VaultRewardRouting
{
    private const int DucketsActivityPointType = 0;

    public static bool TryGetRewardType(CurrencyKind currency, out VaultRewardType type)
    {
        if (currency.CurrencyType == CurrencyType.Credits)
        {
            type = VaultRewardType.Credits;
            return true;
        }

        if (
            currency.CurrencyType == CurrencyType.ActivityPoints
            && currency.ActivityPointType == DucketsActivityPointType
        )
        {
            type = VaultRewardType.Duckets;
            return true;
        }

        type = default;
        return false;
    }

    public static CurrencyKind ToCurrencyKind(VaultRewardType type) =>
        type switch
        {
            VaultRewardType.Credits => new CurrencyKind { CurrencyType = CurrencyType.Credits },
            VaultRewardType.Duckets => new CurrencyKind
            {
                CurrencyType = CurrencyType.ActivityPoints,
                ActivityPointType = DucketsActivityPointType,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Not a currency."),
        };
}
