namespace Vortex.Primitives.Crypto;

/// <summary>
/// Reads the address out of a wallet's signature — the one thing a caller needs in order to accept
/// "this address is mine" as proved rather than claimed.
/// </summary>
/// <remarks>
/// <para>
/// Its own interface, and not another method on <see cref="IHotelWalletService" />. That one hands
/// out the wallets the HOTEL holds; this one checks a wallet the PLAYER holds, and the two have
/// opposite trust directions. Folding the second into the first would have been one fewer file and
/// a name that lied about half of what it did.
/// </para>
/// <para>
/// It exists at all because <c>Vortex.WebApi</c> knows only <c>Vortex.Primitives</c> and
/// <c>Vortex.Database</c>, the same way it reaches the shop and the chain. The arithmetic lives in
/// <c>Vortex.Crypto</c>, where it is pinned against vectors from <c>ethers</c>.
/// </para>
/// </remarks>
public interface IWalletProof
{
    /// <summary>
    /// The EIP-55 address that produced <paramref name="signature" /> over
    /// <paramref name="message" />, or <c>null</c> if the signature is malformed or recovers
    /// nothing.
    /// </summary>
    /// <remarks>
    /// Recovery always yields SOME address for a well-formed signature — that is how ECDSA works.
    /// Whether it is the right one is settled by what was signed, so the caller has to have named
    /// the message itself and must never accept one the client chose.
    /// </remarks>
    public string? RecoverSigner(string message, string? signature);

    /// <summary>Whether <paramref name="candidate" /> is a well-formed address.</summary>
    public bool IsAddress(string? candidate);

    /// <summary><paramref name="address" /> in the EIP-55 mixed case every wallet displays.</summary>
    public string ToChecksumAddress(string address);
}
