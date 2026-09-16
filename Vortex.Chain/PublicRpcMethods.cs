using System.Collections.Frozen;
using System.Collections.Generic;

namespace Vortex.Chain;

/// <summary>
/// The JSON-RPC methods a player's browser is allowed to reach through the hotel.
///
/// <para>
/// This exists because anvil cannot be told to stop serving <c>anvil_setBalance</c> and
/// <c>anvil_impersonateAccount</c>. Publishing its port would let anybody mint themselves anything
/// or act as the treasury with one <c>curl</c>, and a hotel running a contest on those balances
/// would have no contest. So the node stays on the compose network and this is the only way in.
/// </para>
///
/// <para>
/// <b>An allowlist, and never a denylist.</b> A denylist has to name every dangerous namespace that
/// exists now AND every one added later; the first node upgrade that introduces a new one fails
/// open, silently, with no way to notice. This fails closed: a method nobody thought about is
/// refused, and the cost of that is a line added here on purpose.
/// </para>
///
/// <para>
/// Two entries are worth justifying because they look like the dangerous ones.
/// <c>eth_sendRawTransaction</c> is allowed: the transaction is already signed by whoever owns the
/// key, so broadcasting it grants nothing — refusing it would only stop a player from moving their
/// own NFT out of their own wallet. <c>eth_sendTransaction</c> is refused for the opposite reason:
/// it asks the NODE to sign, from an account the node has unlocked, which on anvil is every genesis
/// account including the treasury. <c>eth_accounts</c> goes with it, since it is the list of
/// accounts that could be asked.
/// </para>
/// </summary>
public static class PublicRpcMethods
{
    /// <summary>
    /// Reads, fee estimation and broadcasting: what a wallet needs to add the network, show a
    /// balance, price a transaction and send one it signed itself.
    /// </summary>
    public static readonly FrozenSet<string> Allowed = new HashSet<string>(
        [
            // Identity and head of chain — what `wallet_addEthereumChain` checks first.
            "eth_chainId",
            "net_version",
            "web3_clientVersion",
            "eth_blockNumber",
            // Reads.
            "eth_getBalance",
            "eth_call",
            "eth_getCode",
            "eth_getStorageAt",
            "eth_getLogs",
            "eth_getTransactionCount",
            "eth_getBlockByNumber",
            "eth_getBlockByHash",
            "eth_getTransactionByHash",
            "eth_getTransactionReceipt",
            // Pricing a transaction.
            "eth_estimateGas",
            "eth_gasPrice",
            "eth_maxPriorityFeePerGas",
            "eth_feeHistory",
            // Sending one the caller signed. See the remarks.
            "eth_sendRawTransaction",
        ],
        System.StringComparer.Ordinal
    ).ToFrozenSet(System.StringComparer.Ordinal);

    public static bool IsAllowed(string? method) =>
        !string.IsNullOrEmpty(method) && Allowed.Contains(method);
}
