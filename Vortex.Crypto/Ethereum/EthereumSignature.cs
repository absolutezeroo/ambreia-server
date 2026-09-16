using System;
using System.Buffers;
using System.Globalization;
using System.Text;
using Org.BouncyCastle.Asn1.Sec;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math.EC;
using Org.BouncyCastle.Utilities;
using BigInteger = Org.BouncyCastle.Math.BigInteger;

namespace Vortex.Crypto.Ethereum;

/// <summary>
/// Recovers the Ethereum address that produced a wallet's <c>personal_sign</c> signature, which is
/// the whole of how a player proves an address is theirs: the hotel hands out a nonce, the wallet
/// signs it, and the address that comes back out of the signature is the one that signed. Nothing is
/// asked of any chain -- this is arithmetic, and it works with the node down.
///
/// <para>
/// Written out rather than taken from a package, for the reason <see cref="TotpCodes" /> gives:
/// EIP-191 prefixing and SEC 1 section 4.1.6 key recovery are a frozen specification, secp256k1 and
/// Keccak are already in the BouncyCastle this assembly references, and the alternative -- Nethereum
/// -- is fifteen packages and a second JSON serializer for a hundred lines. What it is NOT is
/// guesswork: <c>EthereumSignatureTests</c> pins every method here against vectors produced by
/// <c>ethers</c>, so a mistake in this file fails a test rather than linking a wallet to the wrong
/// player.
/// </para>
///
/// <para>
/// The message signed is plain readable text with a nonce in it, not a full EIP-4361 (Sign-In With
/// Ethereum) statement. A wallet shows the signer exactly the bytes below, so the two properties
/// that matter -- the signer can read what they are agreeing to, and a captured signature cannot be
/// replayed -- are already there. EIP-4361 adds a domain, a chain id and an expiry, which is what to
/// reach for if this ever has to be verified by somebody who is not this hotel.
/// </para>
/// </summary>
public static class EthereumSignature
{
    /// <summary>
    /// EIP-191's version byte. 0x19 is what separates a signed message from a signed transaction,
    /// and it is the reason a wallet cannot be tricked into signing a transfer while it believes it
    /// is signing a login.
    ///
    /// <para>
    /// Spelled as a number rather than written into the string beside the text. Both of the obvious
    /// ways to do that are traps: pasted in raw it is an invisible control character sitting between
    /// the quote and the E, which reads as nothing and survives exactly until an editor or an
    /// encoding conversion drops it; written as an escape it is one stray backslash away from being
    /// the seven literal characters <c></c> instead. Either mistake compiles, and both take
    /// every signature check in the hotel with them -- the only witness is
    /// <c>EthereumSignatureTests</c>, which is why that file pins the digest on its own.
    /// </para>
    /// </summary>
    private const byte EIP191_VERSION_BYTE = 0x19;

    /// <summary>The EIP-191 personal-message prefix.</summary>
    private static readonly string PERSONAL_MESSAGE_PREFIX =
        (char)EIP191_VERSION_BYTE + "Ethereum Signed Message:\n";

    private const int SIGNATURE_BYTES = 65;
    private const int ADDRESS_BYTES = 20;

    private static readonly X9ECParameters CURVE_PARAMETERS = SecNamedCurves.GetByName("secp256k1");

    /// <summary>
    /// secp256k1. <c>internal</c> rather than private because <see cref="HotelWallets" /> derives
    /// keys on the same curve and multiplies the same generator: a second copy of these four
    /// parameters is a second thing to get wrong.
    /// </summary>
    internal static readonly ECDomainParameters CURVE = new(
        CURVE_PARAMETERS.Curve,
        CURVE_PARAMETERS.G,
        CURVE_PARAMETERS.N,
        CURVE_PARAMETERS.H
    );

    /// <summary>
    /// EIP-2: of the two signatures that satisfy any message, only the one with the low <c>s</c> is
    /// accepted. Both recover the same address, so this is not a security boundary here -- it is
    /// what stops the same authorisation arriving twice wearing two different signatures.
    /// </summary>
    private static readonly BigInteger HALF_CURVE_ORDER = CURVE_PARAMETERS.N.ShiftRight(1);

    /// <summary>
    /// The address that signed <paramref name="message" />, EIP-55 checksummed, or <c>null</c> if
    /// the signature is malformed or recovers nothing. Every rejection answers <c>null</c> rather
    /// than throwing: this is fed straight from a request body, where a bad value is the normal
    /// case and not an exceptional one.
    /// </summary>
    public static string? RecoverAddress(string? message, string? signature)
    {
        if (message is null || string.IsNullOrWhiteSpace(signature))
        {
            return null;
        }

        byte[]? raw = ParseHex(signature, SIGNATURE_BYTES);

        if (raw is null)
        {
            return null;
        }

        BigInteger r = new(1, raw, 0, 32);
        BigInteger s = new(1, raw, 32, 32);
        int recoveryId = raw[64] switch
        {
            27 or 28 => raw[64] - 27,

            // Some wallets hand back the raw recovery bit instead of the 27-based `v` yellow-paper
            // value. Both are in the wild for `personal_sign`, and telling them apart costs nothing.
            0 or 1 => raw[64],
            _ => -1,
        };

        if (recoveryId < 0)
        {
            return null;
        }

        if (r.SignValue <= 0 || s.SignValue <= 0)
        {
            return null;
        }

        if (r.CompareTo(CURVE.N) >= 0 || s.CompareTo(HALF_CURVE_ORDER) > 0)
        {
            return null;
        }

        ECPoint? publicKey = Recover(HashPersonalMessage(message), r, s, recoveryId);

        return publicKey is null ? null : ToChecksumAddress(AddressOf(publicKey));
    }

    /// <summary>
    /// Whether <paramref name="candidate" /> is a well-formed <c>0x</c> address. Case is not
    /// checked: a wallet may hand over an all-lowercase address, and rejecting that would reject
    /// half the wallets in existence over a display convention.
    /// </summary>
    public static bool IsAddress(string? candidate) =>
        ParseHex(candidate, ADDRESS_BYTES) is not null;

    /// <summary>
    /// <paramref name="address" /> in EIP-55 mixed case, which is the form every explorer and wallet
    /// displays. Storing this rather than the lowercase means a comparison has to be
    /// case-insensitive, so callers that compare addresses normalise through here first.
    /// </summary>
    public static string ToChecksumAddress(string address)
    {
        byte[]? raw = ParseHex(address, ADDRESS_BYTES);

        if (raw is null)
        {
            return address;
        }

        string lower = Convert.ToHexString(raw).ToLowerInvariant();

        // The checksum is the Keccak of the ASCII of the LOWERCASE hex -- not of the address bytes.
        // Hashing the bytes instead produces a plausible-looking address that no wallet agrees with.
        byte[] hash = Keccak256(Encoding.ASCII.GetBytes(lower));
        StringBuilder result = new("0x", 2 + lower.Length);

        for (int index = 0; index < lower.Length; index++)
        {
            char character = lower[index];
            int nibble = (hash[index / 2] >> (index % 2 == 0 ? 4 : 0)) & 0xF;

            result.Append(
                character is >= 'a' and <= 'f' && nibble >= 8
                    ? char.ToUpperInvariant(character)
                    : character
            );
        }

        return result.ToString();
    }

    /// <summary>
    /// The 32-byte Keccak-256 digest -- the original Keccak padding Ethereum froze on, which is
    /// <see cref="KeccakDigest" /> and not <c>Sha3Digest</c>. The two differ by one padding byte and
    /// agree on nothing, so picking the wrong one here would recover a valid address belonging to
    /// nobody.
    /// </summary>
    public static byte[] Keccak256(byte[] input)
    {
        KeccakDigest digest = new(256);
        byte[] output = new byte[digest.GetDigestSize()];

        digest.BlockUpdate(input, 0, input.Length);
        digest.DoFinal(output, 0);

        return output;
    }

    /// <summary>
    /// The digest a wallet actually signs for <c>personal_sign</c>: Keccak over the prefix, the
    /// message's length in BYTES rendered as decimal, and the message. Bytes and not characters --
    /// an accented or emoji-bearing message is longer than it looks, and counting characters there
    /// yields a hash no wallet produced.
    /// </summary>
    public static byte[] HashPersonalMessage(string message)
    {
        byte[] body = Encoding.UTF8.GetBytes(message);
        byte[] prefix = Encoding.UTF8.GetBytes(
            PERSONAL_MESSAGE_PREFIX + body.Length.ToString(CultureInfo.InvariantCulture)
        );

        byte[] prefixed = new byte[prefix.Length + body.Length];

        Buffer.BlockCopy(prefix, 0, prefixed, 0, prefix.Length);
        Buffer.BlockCopy(body, 0, prefixed, prefix.Length, body.Length);

        return Keccak256(prefixed);
    }

    /// <summary>
    /// SEC 1, section 4.1.6. <paramref name="recoveryId" /> says which of the two points with
    /// x-coordinate <paramref name="r" /> was used and which of its two y values, so the public key
    /// falls out of the signature without ever having been sent.
    /// </summary>
    internal static ECPoint? Recover(byte[] messageHash, BigInteger r, BigInteger s, int recoveryId)
    {
        BigInteger order = CURVE.N;
        BigInteger fieldPrime = CURVE.Curve.Field.Characteristic;

        // `recoveryId >> 1` is the rare case where the signer's x-coordinate overflowed the curve
        // order and wrapped. `personal_sign` only ever hands over 0 or 1, so this term is zero in
        // practice; it is here because dropping it turns a once-in-2^128 signature into a silent
        // rejection rather than a recovery.
        BigInteger x = r.Add(BigInteger.ValueOf(recoveryId / 2).Multiply(order));

        if (x.CompareTo(fieldPrime) >= 0)
        {
            return null;
        }

        ECPoint point;

        try
        {
            point = DecompressPoint(x, (recoveryId & 1) == 1);
        }
        catch (ArgumentException)
        {
            // No y satisfies the curve for this x: the signature names a point that does not exist.
            return null;
        }

        if (!point.Multiply(order).IsInfinity)
        {
            return null;
        }

        BigInteger e = new(1, messageHash);
        BigInteger rInverse = r.ModInverse(order);
        BigInteger srInverse = rInverse.Multiply(s).Mod(order);
        BigInteger eInverse = BigInteger.Zero.Subtract(e).Mod(order);
        BigInteger eInverserInverse = rInverse.Multiply(eInverse).Mod(order);

        return ECAlgorithms.SumOfTwoMultiplies(CURVE.G, eInverserInverse, point, srInverse);
    }

    private static ECPoint DecompressPoint(BigInteger x, bool yBit)
    {
        int size = (CURVE.Curve.FieldSize + 7) / 8;
        byte[] encoded = new byte[size + 1];

        encoded[0] = (byte)(yBit ? 0x03 : 0x02);
        Buffer.BlockCopy(BigIntegers.AsUnsignedByteArray(size, x), 0, encoded, 1, size);

        return CURVE.Curve.DecodePoint(encoded);
    }

    /// <summary>
    /// The last 20 bytes of the Keccak of the UNCOMPRESSED public key with its <c>0x04</c> tag
    /// dropped. Leaving the tag in shifts every byte and yields an address that is well-formed and
    /// wrong.
    /// </summary>
    internal static string AddressOf(ECPoint publicKey)
    {
        byte[] encoded = publicKey.Normalize().GetEncoded(false);
        byte[] hash = Keccak256(encoded[1..]);

        return "0x" + Convert.ToHexString(hash[^ADDRESS_BYTES..]).ToLowerInvariant();
    }

    /// <summary>
    /// <paramref name="value" /> as exactly <paramref name="expectedBytes" /> bytes, with or without
    /// the <c>0x</c>, or <c>null</c>. A length check as well as a parse: a 64-byte signature is a
    /// signature with its recovery byte missing, not a short one to pad.
    /// </summary>
    private static byte[]? ParseHex(string? value, int expectedBytes)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        ReadOnlySpan<char> digits = value.AsSpan().Trim();

        if (digits.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            digits = digits[2..];
        }

        if (digits.Length != expectedBytes * 2)
        {
            return null;
        }

        byte[] raw = new byte[expectedBytes];

        return Convert.FromHexString(digits, raw, out _, out _) == OperationStatus.Done
            ? raw
            : null;
    }
}
