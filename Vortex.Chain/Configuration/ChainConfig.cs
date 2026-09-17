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
    /// The endpoint a PLAYER'S WALLET is told to use — this API's own filtered proxy, as seen from
    /// the outside, e.g. <c>https://webapi.example.com/api/public/chain/rpc</c>. Never the node.
    ///
    /// <para>
    /// Empty means "work it out from the request", which is right on one hop and wrong on two. The
    /// web API derives the URL from <c>Host</c> and <c>X-Forwarded-Proto</c>, and when the site
    /// proxies <c>/api</c> to the API over plain HTTP that inner hop sets the header to <c>http</c>
    /// — truthfully, for its own leg, and uselessly for a wallet, which refuses a plain-HTTP RPC
    /// endpoint outright. There is no header that survives an arbitrary proxy chain intact, so past
    /// one hop the hotel has to be told rather than guess.
    /// </para>
    /// </summary>
    public string? PublicRpcUrl { get; init; }

    /// <summary>
    /// Signed into every transaction, and what a player's wallet has to be pointed at.
    ///
    /// <para>
    /// <b>NOT anvil's 31337, on purpose.</b> A wallet checks the id against the public chain
    /// registry before it will add a network, and 31337 is taken there — it is GoChain Testnet,
    /// whose native coin is <c>GO</c>. Offering it under any other symbol makes MetaMask refuse the
    /// network with "the native token symbol does not match the expected symbol for this chain ID",
    /// which reads like a misconfiguration of this hotel and is not one.
    /// </para>
    /// <para>
    /// 867839 is unregistered (<c>ethereum-lists/chains</c> has no <c>eip155-867839</c>), and it
    /// spells VORTEX on a phone keypad, which is the only reason it is that number rather than any
    /// other free one. A hotel that changes it must pick another the registry does not have, or the
    /// same wall comes back wearing a different chain's name.
    /// </para>
    /// <para>
    /// Changing it means the chain has to be wiped: an id is signed into every transaction already
    /// on it. The contract addresses survive — they come from the treasury's address and nonce, not
    /// from the chain.
    /// </para>
    /// </summary>
    public long ChainId { get; init; } = 867839;

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

    /// <summary>
    /// What the Collectors Guild charges, in silver, to move a player's Relics out to their own
    /// wallet. Zero is free.
    ///
    /// <para>
    /// A price living in the chain section on purpose, rather than in a configuration class of its
    /// own: the transfer door exists only while a chain does, so this is turned on and off by the
    /// same operator at the same moment as everything above it.
    /// </para>
    /// <para>
    /// Charged once per transfer and not per Relic, because the message that asks for it carries one
    /// address and no item count — the client's button moves the whole collection at once.
    /// </para>
    /// </summary>
    public int TransferFeeSilver { get; init; } = 10;
}
