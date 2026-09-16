using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BigInteger = Org.BouncyCastle.Math.BigInteger;
// System.Security.Cryptography declares an ECPoint of its own -- a pair of byte arrays, nothing to
// do with curve arithmetic -- and this file needs that namespace for HMACSHA256 and ZeroMemory. The
// alias is what keeps the two apart.
using ECPoint = Org.BouncyCastle.Math.EC.ECPoint;

namespace Vortex.Crypto.Ethereum;

/// <summary>
/// The hotel's custodial wallets: one real secp256k1 key per player, derived from a single master
/// seed, so that every player has an on-chain address without installing anything. This is what the
/// official client calls the <i>Stardust</i> wallet -- <c>CollectibleWalletAddressesMessageEvent</c>
/// reads one of these before it reads any self-custody address, which is the order that says which
/// of the two the hotel provides and which the player brings.
///
/// <para>
/// Derivation is a keyed hash, not BIP-32. The hotel signs on a player's behalf and never hands the
/// key over, so the one thing a hierarchical-deterministic path would buy -- importing the whole
/// tree into an off-the-shelf wallet from one mnemonic -- is not a thing anyone here does, and it
/// costs HMAC-SHA512 chaining, point addition and ser32/ser256 to get. If an operator ever needs
/// that, the seed stays the root and BIP-44 slots in beside this as a second scheme; the addresses
/// would differ, which is why the label below carries a version.
/// </para>
///
/// <para>
/// <b>The seed is the hotel's treasury.</b> Whoever holds it holds every player's key and can move
/// everything those wallets contain. It belongs in the environment beside the RSA handshake key,
/// never in a committed file, and <c>CryptoConfigValidator</c> refuses to start on a placeholder
/// outside Development for exactly that reason. Losing it is worse than leaking it: the addresses
/// are unrecoverable and every balance on them is stranded, because nothing anywhere else knows the
/// keys.
/// </para>
/// </summary>
public static class HotelWallets
{
    /// <summary>
    /// The domain label mixed into every derivation. Versioned because it is part of the address:
    /// change this string and every player's wallet becomes a different, empty one.
    /// </summary>
    private const string DERIVATION_LABEL = "vortex.stardust.v1";

    /// <summary>
    /// 32 bytes. Shorter and the derived keys are only as unguessable as the seed, which is the one
    /// thing standing between a stranger and every player's balance.
    /// </summary>
    public const int MASTER_SEED_BYTES = 32;

    /// <summary>A secp256k1 private key is exactly this, no more and no less.</summary>
    public const int PRIVATE_KEY_BYTES = 32;

    /// <summary>A fresh master seed, for an operator who has none.</summary>
    public static byte[] GenerateMasterSeed() => RandomNumberGenerator.GetBytes(MASTER_SEED_BYTES);

    /// <summary>
    /// The private key for <paramref name="playerId" />, as 32 bytes.
    ///
    /// <para>
    /// The counter is rejection sampling, not decoration. A 256-bit hash is very slightly larger
    /// than secp256k1's order, so a raw digest is not a uniform scalar and a vanishingly rare one is
    /// not a scalar at all. Reducing modulo <c>n</c> instead would be the usual shortcut and would
    /// skew the distribution; rehashing is exact and, at a probability of roughly 2^-128 per
    /// attempt, never actually loops.
    /// </para>
    /// </summary>
    public static byte[] DerivePrivateKey(ReadOnlySpan<byte> masterSeed, int playerId)
    {
        if (masterSeed.Length < MASTER_SEED_BYTES)
        {
            throw new ArgumentException(
                $"The wallet master seed must be at least {MASTER_SEED_BYTES} bytes; got "
                    + $"{masterSeed.Length}.",
                nameof(masterSeed)
            );
        }

        if (playerId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(playerId),
                playerId,
                "A wallet is derived for a real player; there is no player 0."
            );
        }

        for (int counter = 0; counter < 256; counter++)
        {
            byte[] material = HMACSHA256.HashData(
                masterSeed,
                Encoding.UTF8.GetBytes(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{DERIVATION_LABEL}/{playerId}/{counter}"
                    )
                )
            );

            BigInteger candidate = new(1, material);

            if (candidate.SignValue > 0 && candidate.CompareTo(EthereumSignature.CURVE.N) < 0)
            {
                return material;
            }
        }

        // Unreachable short of a broken HMAC: 256 consecutive digests would have to land in a range
        // that covers about 2^-128 of the output. Throwing rather than returning something is the
        // only honest answer if it ever happens.
        throw new CryptographicException(
            $"Could not derive a valid secp256k1 key for player {playerId} in 256 attempts."
        );
    }

    /// <summary>
    /// The EIP-55 address a private key controls: <c>k * G</c>, then the same Keccak-of-the-public-
    /// key that <see cref="EthereumSignature" /> ends on.
    ///
    /// <para>
    /// Separate from <see cref="DeriveAddress" /> because it is the only half of a derivation that
    /// can be checked against anything: the seed-to-key step is this hotel's own scheme and has no
    /// reference implementation, while key-to-address is the same arithmetic every wallet performs,
    /// so <c>HotelWalletsTests</c> pins it against addresses <c>ethers</c> produced. It is also what
    /// signing a transaction from a derived wallet will need.
    /// </para>
    /// </summary>
    public static string AddressOfPrivateKey(ReadOnlySpan<byte> privateKey)
    {
        BigInteger scalar = new(1, privateKey.ToArray());

        if (scalar.SignValue <= 0 || scalar.CompareTo(EthereumSignature.CURVE.N) >= 0)
        {
            throw new ArgumentException(
                "Not a valid secp256k1 private key: the scalar must be in [1, n-1].",
                nameof(privateKey)
            );
        }

        ECPoint publicKey = EthereumSignature.CURVE.G.Multiply(scalar);

        return EthereumSignature.ToChecksumAddress(EthereumSignature.AddressOf(publicKey));
    }

    /// <summary>
    /// The EIP-55 address for <paramref name="playerId" />, which is the value the client is handed
    /// as its Stardust wallet.
    /// </summary>
    public static string DeriveAddress(ReadOnlySpan<byte> masterSeed, int playerId)
    {
        byte[] privateKey = DerivePrivateKey(masterSeed, playerId);

        try
        {
            return AddressOfPrivateKey(privateKey);
        }
        finally
        {
            // The address is public; the key that produced it is not, and it has no business
            // outliving this call on the heap.
            CryptographicOperations.ZeroMemory(privateKey);
        }
    }

    /// <summary>
    /// <paramref name="configured" /> as seed bytes, or <c>null</c> if it is absent, not hex, or too
    /// short. Null rather than an exception: an unset seed means the hotel simply has no on-chain
    /// wallets, which is a supported state and not a fault.
    /// </summary>
    public static byte[]? ParseMasterSeed(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return null;
        }

        ReadOnlySpan<char> digits = configured.AsSpan().Trim();

        if (digits.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            digits = digits[2..];
        }

        if (digits.Length < MASTER_SEED_BYTES * 2 || digits.Length % 2 != 0)
        {
            return null;
        }

        byte[] seed = new byte[digits.Length / 2];

        return
            Convert.FromHexString(digits, seed, out _, out _) == System.Buffers.OperationStatus.Done
            ? seed
            : null;
    }

    /// <summary>
    /// A configured private key -- the treasury's -- as 32 bytes, or <c>null</c> if it is absent,
    /// not hex, not exactly 32 bytes, or not a scalar the curve accepts.
    ///
    /// <para>
    /// Separate from <see cref="ParseMasterSeed" /> even though the two read the same characters,
    /// because they do not accept the same thing. A seed is entropy and MORE of it is fine; a
    /// private key is a scalar in [1, n-1] and 40 bytes of hex is not a longer one, it is a mistake
    /// — one that would be accepted here and then fail much later, with an address nobody expected.
    /// </para>
    /// </summary>
    public static byte[]? ParsePrivateKey(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return null;
        }

        ReadOnlySpan<char> digits = configured.AsSpan().Trim();

        if (digits.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            digits = digits[2..];
        }

        if (digits.Length != PRIVATE_KEY_BYTES * 2)
        {
            return null;
        }

        byte[] key = new byte[PRIVATE_KEY_BYTES];

        if (Convert.FromHexString(digits, key, out _, out _) != System.Buffers.OperationStatus.Done)
        {
            return null;
        }

        BigInteger scalar = new(1, key);

        return scalar.SignValue > 0 && scalar.CompareTo(EthereumSignature.CURVE.N) < 0 ? key : null;
    }
}
