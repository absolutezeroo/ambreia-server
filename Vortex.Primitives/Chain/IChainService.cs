using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Vortex.Primitives.Chain;

/// <summary>
/// What a wallet needs to add the hotel's network and find its contracts. Everything here is
/// public by nature — it is printed in the player's own wallet the moment they connect.
/// </summary>
/// <param name="ChainId">Signed into every transaction; what stops one chain's being replayed on another.</param>
/// <param name="CurrencyName">What the wallet calls the native coin. Not VortexCoin: that is the ERC-20.</param>
/// <param name="CurrencySymbol">Its ticker, as the wallet displays it.</param>
/// <param name="CoinAddress">VortexCoin, the ERC-20 the contest is counted in.</param>
/// <param name="CollectibleAddress">VortexCollectible, the Relics.</param>
/// <param name="PublicRpcUrl">
/// What a wallet is told to call, when an operator has said so. <c>null</c> leaves the web API to
/// work it out from the request, which only holds while nothing proxies it twice — see
/// <c>ChainConfig.PublicRpcUrl</c>.
/// </param>
public readonly record struct ChainInfo(
    long ChainId,
    string CurrencyName,
    string CurrencySymbol,
    string? CoinAddress,
    string? CollectibleAddress,
    string? PublicRpcUrl
);

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

    /// <summary>
    /// What the hotel charges, in silver, to move a player's Relics out to their own wallet.
    ///
    /// <para>
    /// A price on a chain interface reads oddly, and it is here for a reason a reader can check: the
    /// packet handler that answers <c>GetNftTransferFee</c> lives in an assembly that references
    /// primitives and nothing else, so this is the only place both it and the configuration can see.
    /// </para>
    /// </summary>
    public int RelicTransferFeeSilver { get; }

    /// <summary>What a player's wallet needs in order to add this network and find the contracts.</summary>
    public ChainInfo GetInfo();

    /// <summary>
    /// Whether <paramref name="method" /> may be forwarded from a browser.
    ///
    /// <para>
    /// Asked through the interface so the web API can answer a refused method with its own status
    /// code without referencing the chain assembly — the list itself stays beside the thing that
    /// holds the node's address, where a second caller cannot get past it.
    /// </para>
    /// </summary>
    public bool IsPublicMethodAllowed(string? method);

    /// <summary>
    /// Forwards one JSON-RPC call from a player's browser to the node, if the method is on the
    /// allowlist.
    ///
    /// <para>
    /// This is the ONLY way a browser reaches the chain: the node's port is not published, because
    /// anvil serves <c>anvil_setBalance</c> alongside the standard methods with no way to switch it
    /// off. A refused method throws rather than answering an error object, so a caller cannot
    /// mistake "the hotel will not do that" for "the chain said no".
    /// </para>
    /// </summary>
    /// <exception cref="System.InvalidOperationException">
    /// The method is not allowed, or no chain is configured.
    /// </exception>
    public Task<JsonElement> ForwardPublicCallAsync(
        string method,
        IReadOnlyList<JsonElement> parameters,
        CancellationToken ct
    );

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

    /// <summary>
    /// Destroys the Relic token <paramref name="assetId" />, which the treasury must hold.
    ///
    /// <para>
    /// This is the second half of the door: a Relic comes home by being sent to the treasury on the
    /// chain, and the hotel then burns it rather than keeping it. Holding it instead would leave one
    /// object owned twice — a database row saying the player has it and a token saying the hotel
    /// does — and the next transfer out would revert on an id that already exists.
    /// </para>
    /// </summary>
    public Task<string> BurnRelicAsync(BigInteger assetId, CancellationToken ct);
}
