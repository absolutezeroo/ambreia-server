using System;
using System.Numerics;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Math.EC;
using Org.BouncyCastle.Utilities;
using BcBigInteger = Org.BouncyCastle.Math.BigInteger;

namespace Vortex.Crypto.Ethereum;

/// <summary>
/// What the hotel has to send: a transaction, signed. <see cref="EthereumSignature" /> only ever
/// reads a signature somebody else made -- this produces one, which is the half that moves value and
/// therefore the half where a mistake costs something rather than throwing.
///
/// <para>
/// EIP-1559 (type 2) rather than the legacy form. The extra work over legacy is one type byte and an
/// empty access list, and in exchange this is the shape every current tool and every node treats as
/// native -- including <c>ethers</c>, which is what the tests check the output against byte for
/// byte.
/// </para>
///
/// <para>
/// Signing is deterministic (RFC 6979, the <see cref="HMacDsaKCalculator" /> below), which is not a
/// convenience: an ECDSA signature made with a random nonce is unreproducible, and an unreproducible
/// signature cannot be pinned to a test vector. It also removes the failure where a weak or repeated
/// random nonce leaks the private key outright, which is the one ECDSA mistake that is famous.
/// </para>
/// </summary>
public static class EthereumTransaction
{
    /// <summary>EIP-2718 type byte for an EIP-1559 transaction.</summary>
    private const byte TRANSACTION_TYPE_EIP1559 = 0x02;

    /// <summary>
    /// The fields of the transaction to sign. Everything is required except the two that have a
    /// meaningful empty value -- sending no ether, and calling nothing.
    /// </summary>
    public sealed record Request
    {
        /// <summary>
        /// Signed into the payload, which is what stops a transaction from one chain being replayed
        /// on another (EIP-155's whole purpose, carried into the typed forms).
        /// </summary>
        public required long ChainId { get; init; }

        /// <summary>
        /// The sender's transaction count. The node has it (<c>eth_getTransactionCount</c>); it is a
        /// parameter here because this class does not talk to any node.
        /// </summary>
        public required ulong Nonce { get; init; }

        public required BigInteger MaxPriorityFeePerGas { get; init; }
        public required BigInteger MaxFeePerGas { get; init; }
        public required ulong GasLimit { get; init; }

        /// <summary>
        /// The recipient, <c>0x</c>-prefixed or not. Contract creation -- an absent recipient -- is
        /// deliberately not supported: the hotel's two contracts are deployed once by
        /// <c>chain/deploy.sh</c>, and a server that can create contracts at runtime is a
        /// capability nothing here needs.
        /// </summary>
        public required string To { get; init; }

        public BigInteger Value { get; init; }

        public byte[] Data { get; init; } = [];
    }

    /// <summary>
    /// The raw signed transaction, ready for <c>eth_sendRawTransaction</c>.
    /// </summary>
    public static byte[] Sign(Request request, ReadOnlySpan<byte> privateKey)
    {
        ArgumentNullException.ThrowIfNull(request);

        byte[][] fields = UnsignedFields(request);
        byte[] payload = Rlp.EncodeList(fields);

        // The digest covers the TYPE BYTE as well as the payload. Hashing the payload alone produces
        // a signature the node reads as somebody else's, and the only symptom is a rejected
        // transaction blaming the sender.
        byte[] sigHash = EthereumSignature.Keccak256([TRANSACTION_TYPE_EIP1559, .. payload]);

        (BcBigInteger r, BcBigInteger s, int yParity) = SignHash(sigHash, privateKey);

        byte[] signed = Rlp.EncodeList([
            .. fields,
            Rlp.EncodeInteger(yParity),
            Rlp.EncodeInteger(ToSystemBigInteger(r)),
            Rlp.EncodeInteger(ToSystemBigInteger(s)),
        ]);

        return [TRANSACTION_TYPE_EIP1559, .. signed];
    }

    /// <summary>The nine fields an EIP-1559 payload carries before its signature, in order.</summary>
    private static byte[][] UnsignedFields(Request request)
    {
        if (!EthereumSignature.IsAddress(request.To))
        {
            throw new ArgumentException(
                $"'{request.To}' is not a 20-byte address.",
                nameof(request)
            );
        }

        string digits = request.To.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? request.To[2..]
            : request.To;

        return
        [
            Rlp.EncodeInteger(request.ChainId),
            Rlp.EncodeInteger(request.Nonce),
            Rlp.EncodeInteger(request.MaxPriorityFeePerGas),
            Rlp.EncodeInteger(request.MaxFeePerGas),
            Rlp.EncodeInteger(request.GasLimit),
            // An address is a 20-byte STRING, not an integer: a recipient whose first byte is zero
            // keeps it, where EncodeInteger would strip it and send to a different address entirely.
            Rlp.Encode(Convert.FromHexString(digits)),
            Rlp.EncodeInteger(request.Value),
            Rlp.Encode(request.Data),
            Rlp.EncodeEmptyList(),
        ];
    }

    /// <summary>
    /// Signs an EIP-191 personal message the way a wallet does, and answers the 65-byte
    /// <c>r ‖ s ‖ v</c> a browser would have sent.
    /// </summary>
    /// <remarks>
    /// The inverse of <see cref="EthereumSignature.RecoverAddress" />, and it exists so the link
    /// flow can be exercised end to end in a test without a browser and a wallet extension in the
    /// loop. The hotel never signs on a player's behalf in production — it holds no self-custody
    /// key and would have nothing to sign with.
    /// </remarks>
    public static byte[] SignPersonalMessage(string message, ReadOnlySpan<byte> privateKey)
    {
        (BcBigInteger r, BcBigInteger s, int yParity) = SignHash(
            EthereumSignature.HashPersonalMessage(message),
            privateKey
        );

        byte[] signature = new byte[65];

        BigIntegers.AsUnsignedByteArray(32, r).CopyTo(signature, 0);
        BigIntegers.AsUnsignedByteArray(32, s).CopyTo(signature, 32);

        // 27-based, which is what every wallet emits and what RecoverAddress reads first.
        signature[64] = (byte)(yParity + 27);

        return signature;
    }

    /// <summary>
    /// Signs <paramref name="hash" /> and answers the canonical <c>(r, s, yParity)</c>.
    /// </summary>
    private static (BcBigInteger R, BcBigInteger S, int YParity) SignHash(
        byte[] hash,
        ReadOnlySpan<byte> privateKey
    )
    {
        BcBigInteger scalar = new(1, privateKey.ToArray());

        if (scalar.SignValue <= 0 || scalar.CompareTo(EthereumSignature.CURVE.N) >= 0)
        {
            throw new ArgumentException(
                "Not a valid secp256k1 private key: the scalar must be in [1, n-1].",
                nameof(privateKey)
            );
        }

        ECDsaSigner signer = new(new HMacDsaKCalculator(new Sha256Digest()));

        signer.Init(true, new ECPrivateKeyParameters(scalar, EthereumSignature.CURVE));

        BcBigInteger[] signature = signer.GenerateSignature(hash);
        BcBigInteger r = signature[0];
        BcBigInteger s = signature[1];

        // EIP-2. Of the two signatures that satisfy the hash, only the low-s one is canonical, and
        // flipping s flips the recovery bit with it -- which is why yParity is worked out AFTER this
        // and not before.
        if (s.CompareTo(EthereumSignature.CURVE.N.ShiftRight(1)) > 0)
        {
            s = EthereumSignature.CURVE.N.Subtract(s);
        }

        return (r, s, RecoveryBit(hash, r, s, scalar));
    }

    /// <summary>
    /// Which of the two candidate public keys is the signer's.
    ///
    /// <para>
    /// Found by recovering both and comparing, rather than derived from the ephemeral point: the
    /// signer does not hand that point back, and the comparison reuses the recovery
    /// <see cref="EthereumSignature" /> already has under test. Two point multiplications per
    /// transaction is not a cost worth a second implementation of the same arithmetic.
    /// </para>
    /// </summary>
    private static int RecoveryBit(
        byte[] hash,
        BcBigInteger r,
        BcBigInteger s,
        BcBigInteger privateKey
    )
    {
        ECPoint expected = EthereumSignature.CURVE.G.Multiply(privateKey).Normalize();

        for (int candidate = 0; candidate < 2; candidate++)
        {
            ECPoint? recovered = EthereumSignature.Recover(hash, r, s, candidate);

            if (recovered is not null && recovered.Normalize().Equals(expected))
            {
                return candidate;
            }
        }

        // Unreachable for a signature this class just produced: one of the two candidates is the
        // signer by construction. Throwing says so rather than signing with a guessed bit, which
        // would produce a transaction the node attributes to a stranger.
        throw new InvalidOperationException(
            "Neither recovery candidate matched the signing key; the signature is not usable."
        );
    }

    private static BigInteger ToSystemBigInteger(BcBigInteger value) =>
        new(BigIntegers.AsUnsignedByteArray(value), isUnsigned: true, isBigEndian: true);
}
