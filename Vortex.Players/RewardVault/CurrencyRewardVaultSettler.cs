using System.Collections.Frozen;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Primitives.Commerce;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players.Wallet;
using Vortex.Primitives.RewardVault;
using Vortex.Primitives.RewardVault.Enums;

namespace Vortex.Players.RewardVault;

/// <summary>Credits and duckets: one wallet credit per row, under a receipt derived from the row.</summary>
internal sealed class CurrencyRewardVaultSettler(IGrainFactory grainFactory) : IRewardVaultSettler
{
    public IReadOnlySet<RewardVaultType> Handles { get; } =
        new[] { RewardVaultType.Credits, RewardVaultType.Duckets }.ToFrozenSet();

    public async Task SettleAsync(int playerId, RewardVaultRow row, CancellationToken ct)
    {
        // The result is deliberately ignored: false is "this receipt exists", i.e. an earlier claim
        // paid it and crashed before marking the row. A currency that cannot be credited throws.
        await grainFactory
            .GetPlayerWalletGrain(playerId)
            .CreditOnceAsync(
                [
                    new WalletDebitRequest
                    {
                        CurrencyKind = RewardVaultRouting.ToCurrencyKind(row.RewardType),
                        Amount = row.Amount,
                    },
                ],
                CommerceOperationId.Deterministic(CommerceOperationKind.RewardVault, row.Id),
                CommerceStepKeys.VAULT_CLAIM,
                ct
            )
            .ConfigureAwait(false);
    }
}
