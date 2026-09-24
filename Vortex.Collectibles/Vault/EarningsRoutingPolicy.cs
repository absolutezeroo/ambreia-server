using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using Vortex.Collectibles.Configuration;
using Vortex.Primitives.Vault.Enums;

namespace Vortex.Collectibles.Vault;

/// <summary>
/// The operator switch behind each Earnings category. Wiring a new source is one entry here, one
/// constant in <see cref="EarningsConfig"/> and one line in <c>ConfigKeyCatalog</c>.
/// </summary>
internal static class EarningsRoutingPolicy
{
    private static readonly FrozenDictionary<VaultRewardCategoryType, string> Keys = new Dictionary<
        VaultRewardCategoryType,
        string
    >
    {
        [VaultRewardCategoryType.Achievements] = EarningsConfig.AchievementsToVaultKey,
        [VaultRewardCategoryType.HabboClub] = EarningsConfig.HabboClubToVaultKey,
    }.ToFrozenDictionary();

    public static IReadOnlyCollection<string> AllKeys => Keys.Values;

    public static string KeyFor(VaultRewardCategoryType category) =>
        Keys.TryGetValue(category, out string? key)
            ? key
            : throw new InvalidOperationException(
                $"Earnings category {category} has a source but no routing switch: add it to "
                    + $"{nameof(EarningsRoutingPolicy)}, {nameof(EarningsConfig)} and ConfigKeyCatalog."
            );
}
