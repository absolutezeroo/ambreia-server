using Vortex.Primitives.Crypto;

namespace Vortex.Crypto.Ethereum;

/// <summary>
/// <see cref="IWalletProof" /> over <see cref="EthereumSignature" />.
/// </summary>
/// <remarks>
/// Three forwarding methods and no logic of its own, on purpose: the arithmetic is static and
/// already under test against <c>ethers</c>, and this exists only so a caller that must not
/// reference <c>Vortex.Crypto</c> can still reach it.
/// </remarks>
public sealed class WalletProof : IWalletProof
{
    public string? RecoverSigner(string message, string? signature) =>
        EthereumSignature.RecoverAddress(message, signature);

    public bool IsAddress(string? candidate) => EthereumSignature.IsAddress(candidate);

    public string ToChecksumAddress(string address) => EthereumSignature.ToChecksumAddress(address);
}
