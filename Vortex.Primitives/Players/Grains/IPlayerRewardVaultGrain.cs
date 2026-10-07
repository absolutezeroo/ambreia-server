using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Primitives.Orleans.Snapshots.RewardVault;
using Vortex.Primitives.RewardVault;
using Vortex.Primitives.RewardVault.Enums;

namespace Vortex.Primitives.Players.Grains;

/// <summary>A player's Earnings window: what they earned and have not cashed yet.</summary>
public interface IPlayerRewardVaultGrain : IGrainWithIntegerKey
{
    /// <summary>Unclaimed earnings, one entry per row; the client sums them per category.</summary>
    Task<List<IncomeRewardSnapshot>> GetIncomeRewardsAsync(CancellationToken ct);

    /// <summary>
    /// The one way anything pays into the Earnings window. Kept as a row, or paid straight to the
    /// wallet when the currency has no Earnings type or the operator routed the category direct.
    /// Idempotent on <see cref="IncomeEarning.SourceKey"/> either way.
    /// </summary>
    Task EarnAsync(IncomeEarning earning, CancellationToken ct);

    /// <summary>Cashes one category, or every one for <see cref="RewardVaultCategoryType.All"/>.</summary>
    Task<bool> ClaimAsync(RewardVaultCategoryType category, CancellationToken ct);
}
