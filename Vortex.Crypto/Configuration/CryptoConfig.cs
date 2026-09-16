namespace Vortex.Crypto.Configuration;

public class CryptoConfig
{
    public const string SECTION_NAME = "Vortex:Crypto";

    public required string KeySize { get; init; }
    public required string PublicKey { get; init; }
    public required string PrivateKey { get; init; }
    public required bool EnableServerToClientEncryption { get; init; }

    /// <summary>
    /// Hexadecimal, at least 32 bytes. The root every player's custodial wallet key is derived from
    /// (see <see cref="HotelWallets" />), which makes it the hotel's treasury -- treat it like the
    /// RSA private key and keep it out of committed files.
    ///
    /// <para>
    /// Deliberately NOT <c>required</c>. Unset means the hotel has no on-chain wallets and players
    /// get the keyless placeholder addresses every deployment had before this existed; setting it is
    /// the whole of turning the feature on. Making it required would fail the startup of every hotel
    /// that does not want a chain at all.
    /// </para>
    /// </summary>
    public string? WalletMasterSeed { get; init; }
}
