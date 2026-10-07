using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using Vortex.Primitives.RewardVault.Enums;

namespace Vortex.Players.RewardVault;

/// <summary>
/// The operator switch behind each Earnings category. Wiring a new source is one entry here, one
/// constant in <see cref="EarningsConfig"/> and one line in <c>ConfigKeyCatalog</c>.
/// </summary>
internal static class EarningsRoutingPolicy
{
    private static readonly FrozenDictionary<RewardVaultCategoryType, string> Keys = new Dictionary<
        RewardVaultCategoryType,
        string
    >
    {
        [RewardVaultCategoryType.Achievements] = EarningsConfig.AchievementsToRewardVaultKey,
        [RewardVaultCategoryType.Premium] = EarningsConfig.MembershipToRewardVaultKey,
    }.ToFrozenDictionary();

    public static IReadOnlyCollection<string> AllKeys => Keys.Values;

    public static string KeyFor(RewardVaultCategoryType category) =>
        Keys.TryGetValue(category, out string? key)
            ? key
            : throw new InvalidOperationException(
                $"Earnings category {category} has a source but no routing switch: add it to "
                    + $"{nameof(EarningsRoutingPolicy)}, {nameof(EarningsConfig)} and ConfigKeyCatalog."
            );
}
