namespace Vortex.Chain.Configuration;

/// <summary>
/// Where the hotel's chain is, and what lives on it.
///
/// <para>
/// Every value is optional, and an empty <see cref="RpcUrl" /> is how the whole feature is switched
/// off — which is the default. A hotel that wants no chain runs exactly as it did before, and the
/// service above this answers "off" rather than failing to start.
/// </para>
/// </summary>
public class ChainConfig
{
    public const string SECTION_NAME = "Vortex:Chain";

    /// <summary>
    /// The node's JSON-RPC endpoint, e.g. <c>http://chain:8545</c>. Empty disables the chain.
    /// </summary>
    public string? RpcUrl { get; init; }

    /// <summary>
    /// Signed into every transaction, and what a player's wallet has to be pointed at. 31337 is
    /// anvil's default.
    /// </summary>
    public long ChainId { get; init; } = 31337;

    /// <summary>
    /// <c>VortexCoin</c>'s address. Deterministic for a given treasury — the first contract it
    /// deploys — so it is a constant here rather than something discovered at startup. See
    /// <c>chain/deploy.sh</c>.
    /// </summary>
    public string? CoinAddress { get; init; }

    /// <summary><c>VortexCollectible</c>'s address, the treasury's second deployment.</summary>
    public string? CollectibleAddress { get; init; }

    /// <summary>
    /// What a wallet calls the chain's NATIVE coin — the one that pays for gas, not
    /// <c>VortexCoin</c>. A wallet shows whatever the network declares here, so this is the
    /// difference between a player seeing "0.02 ETH" and "0.02 VTX" beside a transaction on a chain
    /// that has nothing to do with Ethereum.
    /// </summary>
    public string CurrencyName { get; init; } = "Vortex";

    public string CurrencySymbol { get; init; } = "VTX";

    /// <summary>
    /// The account that owns both contracts and pays for every transaction the hotel sends.
    /// Hexadecimal, 32 bytes.
    ///
    /// <para>
    /// A second treasure to guard, and not the same one as
    /// <c>Vortex:Crypto:WalletMasterSeed</c>: the seed is every player's key, this one is the only
    /// account that can mint. Keep both out of committed files.
    /// </para>
    /// </summary>
    public string? TreasuryPrivateKey { get; init; }

    /// <summary>
    /// What the hotel is willing to pay per unit of gas, in wei. The default suits a chain the hotel
    /// runs itself, where the fee is a formality; a public chain needs a real number and probably a
    /// real fee estimator.
    /// </summary>
    public ulong MaxFeePerGasWei { get; init; } = 2_000_000_000;

    public ulong MaxPriorityFeePerGasWei { get; init; } = 1_000_000_000;

    /// <summary>
    /// The gas ceiling for one call. A mint is a few tens of thousands; this leaves room for the
    /// enumerable collectible's bookkeeping, which is the most expensive thing the hotel sends.
    /// </summary>
    public ulong GasLimit { get; init; } = 300_000;

    /// <summary>
    /// How long any single RPC call may take. Short on purpose: these run inside request and packet
    /// handling, and a node that has stopped answering must fail rather than hold a handler open.
    /// </summary>
    public int RequestTimeoutSeconds { get; init; } = 10;
}
