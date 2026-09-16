using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace Vortex.Crypto.Ethereum;

/// <summary>
/// The contract ABI, for the handful of shapes this hotel actually calls: encoding
/// <c>mint(address,uint256)</c> and friends on the way out, and reading a balance, an owner or a
/// list of token ids on the way back.
///
/// <para>
/// Deliberately not a general ABI coder. The full specification covers fixed-point types, nested
/// tuples, arrays of arrays and packed encodings, none of which appear anywhere in
/// <c>VortexCoin</c> or <c>VortexCollectible</c> — it is ten times this size for types nothing here
/// will ever pass. What IS handled is complete for those two contracts, and anything else fails at
/// the call site rather than silently encoding the wrong bytes.
/// </para>
///
/// <para>
/// Two rules carry all of it. Every value occupies a 32-byte word, padded on the LEFT for numbers
/// and addresses and on the RIGHT for raw bytes. A dynamic value (an array, a string) does not sit
/// in its own word: the word holds the OFFSET to where its data begins, counted from the start of
/// the argument block, and the data itself follows every argument's word. Getting that head-and-tail
/// split wrong produces a call the contract decodes into different numbers, not one it rejects.
/// </para>
/// </summary>
public static class AbiCodec
{
    /// <summary>Every ABI value is a multiple of this.</summary>
    public const int WORD_BYTES = 32;

    private const int SELECTOR_BYTES = 4;

    /// <summary>
    /// One encoded argument, and whether it belongs in the head or the tail. The flag is why this is
    /// a type rather than a bare <c>byte[]</c>: a caller holding only the bytes cannot say which of
    /// the two a value is, and the difference decides where it goes.
    /// </summary>
    public readonly record struct Value(byte[] Encoded, bool IsDynamic)
    {
        /// <summary>A 20-byte address, right-aligned in its word.</summary>
        public static Value Address(string address)
        {
            if (!EthereumSignature.IsAddress(address))
            {
                throw new ArgumentException(
                    $"'{address}' is not a 20-byte address.",
                    nameof(address)
                );
            }

            string digits = address.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? address[2..]
                : address;

            byte[] word = new byte[WORD_BYTES];

            Convert.FromHexString(digits).CopyTo(word, WORD_BYTES - 20);

            return new Value(word, false);
        }

        public static Value Uint256(BigInteger value)
        {
            if (value.Sign < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "uint256 cannot carry a negative number."
                );
            }

            byte[] bytes = value.ToByteArray(isUnsigned: true, isBigEndian: true);

            if (bytes.Length > WORD_BYTES)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "The value does not fit in a uint256."
                );
            }

            byte[] word = new byte[WORD_BYTES];

            bytes.CopyTo(word, WORD_BYTES - bytes.Length);

            return new Value(word, false);
        }

        public static Value Bool(bool value) => Uint256(value ? BigInteger.One : BigInteger.Zero);

        /// <summary>A <c>uint256[]</c>: the count, then one word per item.</summary>
        public static Value UintArray(IReadOnlyList<BigInteger> values)
        {
            ArgumentNullException.ThrowIfNull(values);

            byte[] encoded = new byte[WORD_BYTES * (values.Count + 1)];

            Uint256(values.Count).Encoded.CopyTo(encoded, 0);

            for (int index = 0; index < values.Count; index++)
            {
                Uint256(values[index]).Encoded.CopyTo(encoded, WORD_BYTES * (index + 1));
            }

            return new Value(encoded, true);
        }

        /// <summary>An <c>address[]</c>, encoded exactly as a <c>uint256[]</c> of addresses is.</summary>
        public static Value AddressArray(IReadOnlyList<string> addresses)
        {
            ArgumentNullException.ThrowIfNull(addresses);

            byte[] encoded = new byte[WORD_BYTES * (addresses.Count + 1)];

            Uint256(addresses.Count).Encoded.CopyTo(encoded, 0);

            for (int index = 0; index < addresses.Count; index++)
            {
                Address(addresses[index]).Encoded.CopyTo(encoded, WORD_BYTES * (index + 1));
            }

            return new Value(encoded, true);
        }
    }

    /// <summary>
    /// The four-byte selector for <paramref name="signature" /> -- e.g. <c>mint(address,uint256)</c>,
    /// with no parameter names and no spaces, which is what the hash is taken over.
    /// </summary>
    public static byte[] Selector(string signature)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signature);

        return EthereumSignature.Keccak256(Encoding.ASCII.GetBytes(signature))[..SELECTOR_BYTES];
    }

    /// <summary>The complete call data: the selector, then the arguments.</summary>
    public static byte[] EncodeCall(string signature, params Value[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        byte[] selector = Selector(signature);

        // The tail starts after every argument's word, dynamic ones included -- their word holds the
        // offset, so they take up exactly as much of the head as a static value does.
        int tailOffset = arguments.Length * WORD_BYTES;
        List<byte> head = [];
        List<byte> tail = [];

        foreach (Value argument in arguments)
        {
            if (argument.IsDynamic)
            {
                head.AddRange(Value.Uint256(tailOffset + tail.Count).Encoded);
                tail.AddRange(argument.Encoded);
            }
            else
            {
                head.AddRange(argument.Encoded);
            }
        }

        return [.. selector, .. head, .. tail];
    }

    /// <summary>A single <c>uint256</c> answer -- a balance, a supply, a token id.</summary>
    public static BigInteger DecodeUint256(ReadOnlySpan<byte> result)
    {
        if (result.Length < WORD_BYTES)
        {
            throw new ArgumentException(
                $"A uint256 answer is {WORD_BYTES} bytes; got {result.Length}.",
                nameof(result)
            );
        }

        return new BigInteger(result[..WORD_BYTES], isUnsigned: true, isBigEndian: true);
    }

    public static bool DecodeBool(ReadOnlySpan<byte> result) => !DecodeUint256(result).IsZero;

    /// <summary>An <c>address</c> answer, EIP-55 checksummed.</summary>
    public static string DecodeAddress(ReadOnlySpan<byte> result)
    {
        if (result.Length < WORD_BYTES)
        {
            throw new ArgumentException(
                $"An address answer is {WORD_BYTES} bytes; got {result.Length}.",
                nameof(result)
            );
        }

        // The address is the LAST 20 bytes of the word. Taking the first 20 gives twelve zeroes and
        // eight real ones, which is a well-formed address belonging to nobody.
        return EthereumSignature.ToChecksumAddress(
            "0x" + Convert.ToHexString(result.Slice(WORD_BYTES - 20, 20)).ToLowerInvariant()
        );
    }

    /// <summary>
    /// A <c>uint256[]</c> answer -- what <c>tokensOfOwner</c> hands back, and the reason the
    /// collectible contract is enumerable at all.
    /// </summary>
    public static IReadOnlyList<BigInteger> DecodeUintArray(ReadOnlySpan<byte> result)
    {
        // A returned array is dynamic like an argument is: the first word is the offset to its data.
        // Reading the count from word 0 instead works by accident on an empty result and on nothing
        // else, because that offset is almost always 32 and an empty array's count is 0.
        int offset = (int)DecodeUint256(result);
        int count = (int)DecodeUint256(result[offset..]);
        List<BigInteger> values = new(count);

        for (int index = 0; index < count; index++)
        {
            values.Add(DecodeUint256(result[(offset + WORD_BYTES * (index + 1))..]));
        }

        return values;
    }

    /// <summary>A <c>string</c> answer -- a name, a symbol, a token URI.</summary>
    public static string DecodeString(ReadOnlySpan<byte> result)
    {
        int offset = (int)DecodeUint256(result);
        int length = (int)DecodeUint256(result[offset..]);

        // The bytes are padded up to a whole word on the RIGHT; the length says how many of them are
        // the string, and decoding the padding as well appends NUL characters that compare unequal
        // to everything and print as nothing.
        return Encoding.UTF8.GetString(result.Slice(offset + WORD_BYTES, length));
    }
}
