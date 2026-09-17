using System.Threading;
using System.Threading.Tasks;

namespace Vortex.Primitives.Chain;

/// <summary>
/// Pays a player VortexCoin for something they did in the game.
/// </summary>
/// <remarks>
/// <para>
/// The one place the hotel turns "give this player N coins" into a mint. It exists because that
/// sentence has three parts that must never diverge — which address the coins go to (the custodial
/// one, from <c>IHotelWalletService</c>), how many decimals a coin has (eighteen), and which
/// contract call creates them — and they were being spelled out separately everywhere a payout was
/// added. <c>COIN_DECIMALS = 18</c> already existed as a private constant in three files before
/// this interface did.
/// </para>
/// <para>
/// It deals in WHOLE coins because every caller above it does: an operator types "20" into a form,
/// a community goal rung stores "20", a reward track's amount column holds "20". Wei is the
/// contract's unit and stays on this side of the boundary.
/// </para>
/// </remarks>
public interface IChainRewardService
{
    /// <summary>
    /// Whether a payment can succeed at all: there is a chain, and the hotel holds keys for the
    /// addresses it hands out. False means <see cref="PayAsync" /> would mint into an address
    /// nobody can ever spend from, so callers skip rather than queue.
    /// </summary>
    public bool Enabled { get; }

    /// <summary>
    /// Mints <paramref name="wholeCoins" /> VortexCoin to the address the hotel holds for
    /// <paramref name="playerId" />, and answers the transaction hash.
    /// </summary>
    /// <remarks>
    /// Throws when the chain refuses or is unreachable. That is deliberate: a mint fails for
    /// reasons that pass — a node restart, a nonce clash with the Relic doors — so a caller that
    /// owes the reward should retry rather than record a permanent failure. A caller that cannot
    /// retry should check <see cref="Enabled" /> first.
    /// </remarks>
    public Task<string> PayAsync(int playerId, int wholeCoins, CancellationToken ct);
}
