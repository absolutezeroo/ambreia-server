using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Vault.Enums;

namespace Vortex.Collectibles.Vault;

/// <summary>
/// Turns a claimed Earnings row into what it is worth. One implementation per reward type, resolved
/// from DI: the slice that first deposits products adds a product settler and one line in
/// <c>CollectiblesModule</c>, and the vault grain does not change.
/// </summary>
/// <remarks>
/// Returning means the row is paid — now, or by an earlier claim that crashed before the row was
/// marked. Throwing means it is not, and the row stays unclaimed for the next attempt. A settler must
/// be safe to call twice for the same row.
/// </remarks>
internal interface IVaultRewardSettler
{
    IReadOnlySet<VaultRewardType> Handles { get; }

    Task SettleAsync(int playerId, VaultIncomeRow row, CancellationToken ct);
}
