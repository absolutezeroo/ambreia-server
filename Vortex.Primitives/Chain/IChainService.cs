using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace Vortex.Primitives.Chain;

/// <summary>
/// What the rest of the hotel is allowed to ask of the chain. Deliberately four operations and not a
/// node client: reading a player's balance, reading the Relics in their wallet, and the two mints
/// the treasury performs.
///
/// <para>
/// Amounts are in the token's smallest unit -- wei, 18 decimals -- because that is what the contract
/// stores and every conversion to a display figure is a rounding decision that belongs where it is
/// displayed, not here.
/// </para>
/// </summary>
public interface IChainService
{
    /// <summary>
    /// Whether a chain is configured AND usable. False means every call below will refuse, and a
    /// caller should treat the feature as absent rather than broken -- it is the default state.
    /// </summary>
    public bool Enabled { get; }

    /// <summary>The address the hotel mints from, or <c>null</c> when disabled.</summary>
    public string? TreasuryAddress { get; }

    /// <summary>VortexCoin held by <paramref name="address" />, in wei.</summary>
    public Task<BigInteger> GetCoinBalanceAsync(string address, CancellationToken ct);

    /// <summary>
    /// Every Relic token id in <paramref name="address" />'s wallet, read straight off the contract
    /// -- no indexer, because <c>VortexCollectible</c> is enumerable.
    /// </summary>
    public Task<IReadOnlyList<BigInteger>> GetRelicsAsync(string address, CancellationToken ct);

    /// <summary>
    /// Creates <paramref name="amount" /> wei of VortexCoin for <paramref name="to" /> and answers
    /// the transaction hash.
    /// </summary>
    public Task<string> MintCoinAsync(string to, BigInteger amount, CancellationToken ct);

    /// <summary>
    /// Mints the Relic whose hotel asset id is <paramref name="assetId" /> to <paramref name="to" />
    /// and answers the transaction hash. The id is the hotel's own; the contract does not allocate
    /// one, and minting the same asset twice reverts.
    /// </summary>
    public Task<string> MintRelicAsync(string to, BigInteger assetId, CancellationToken ct);
}
