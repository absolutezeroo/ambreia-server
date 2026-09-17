using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vortex.Primitives.Chain;
using Vortex.Primitives.Crypto;

namespace Vortex.Chain;

/// <summary>
/// Turns "pay this player N VortexCoin" into a mint to their custodial address.
/// </summary>
/// <remarks>
/// Deliberately thin. The point is not what it does but that there is only one of it: before this,
/// every feature that paid coin resolved the address itself and wrote its own
/// <c>* BigInteger.Pow(10, 18)</c>, and eighteen is the kind of number that is right in three files
/// and wrong in the fourth for a year.
/// </remarks>
internal sealed class ChainRewardService(
    IChainService chain,
    IHotelWalletService wallets,
    ILogger<ChainRewardService> logger
) : IChainRewardService
{
    /// <summary>Eighteen, as <c>VortexCoin.sol</c> declares. The only copy of this number.</summary>
    private const int COIN_DECIMALS = 18;

    public bool Enabled => chain.Enabled && wallets.Enabled;

    public async Task<string> PayAsync(int playerId, int wholeCoins, CancellationToken ct)
    {
        if (wholeCoins <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(wholeCoins),
                wholeCoins,
                "A coin reward has to be positive."
            );
        }

        if (!Enabled)
        {
            // Not a silent no-op returning an empty hash: a caller that gets a hash back records the
            // reward as delivered, and delivering nothing into an unspendable address is exactly the
            // failure that would go unnoticed. Callers that cannot retry check Enabled first.
            throw new InvalidOperationException(
                "chain_not_configured: this hotel has no chain, or holds no keys for the addresses "
                    + "it hands out, so coin rewards cannot be paid."
            );
        }

        string address = wallets.GetAddress(playerId);

        string hash = await chain
            .MintCoinAsync(
                address,
                new BigInteger(wholeCoins) * BigInteger.Pow(10, COIN_DECIMALS),
                ct
            )
            .ConfigureAwait(false);

        logger.LogInformation(
            "Paid {Amount} VTX to player {PlayerId} at {Address} ({Hash}).",
            wholeCoins,
            playerId,
            address,
            hash
        );

        return hash;
    }
}
