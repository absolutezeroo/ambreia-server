using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Primitives.Orleans.Snapshots.Vault;
using Vortex.Primitives.Vault;
using Vortex.Primitives.Vault.Enums;

namespace Vortex.Primitives.Players.Grains;

/// <summary>A player's Earnings window: what they earned and have not cashed yet.</summary>
public interface IPlayerVaultGrain : IGrainWithIntegerKey
{
    /// <summary>Unclaimed earnings, one entry per row; the client sums them per category.</summary>
    Task<List<IncomeRewardSnapshot>> GetIncomeRewardsAsync(CancellationToken ct);

    /// <summary>
    /// The one way anything pays into the Earnings window. Kept as a row, or paid straight to the
    /// wallet when the currency has no Earnings type or the operator routed the category direct.
    /// Idempotent on <see cref="IncomeEarning.SourceKey"/> either way.
    /// </summary>
    Task EarnAsync(IncomeEarning earning, CancellationToken ct);

    /// <summary>Cashes one category, or every one for <see cref="VaultRewardCategoryType.All"/>.</summary>
    Task<bool> ClaimAsync(VaultRewardCategoryType category, CancellationToken ct);
}
