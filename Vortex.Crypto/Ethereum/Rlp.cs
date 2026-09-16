using System;
using System.Collections.Generic;
using System.Numerics;

namespace Vortex.Crypto.Ethereum;

/// <summary>
/// RLP, Ethereum's one serialisation format -- the shape a transaction is hashed and signed in.
/// Encoding only: nothing here reads a chain's answers back, because the node speaks JSON-RPC and
/// hands those over already decoded.
///
/// <para>
/// The whole specification is two rules, and they are why this is forty lines rather than a package:
/// a byte string is itself when it is one byte below 0x80, otherwise a length prefix then the bytes;
/// a list is the concatenation of its encoded items behind a length prefix of its own. The prefixes
/// differ for payloads over 55 bytes, where the length is itself encoded.
/// </para>
///
/// <para>
/// The trap is <see cref="EncodeInteger" />. RLP has no integers -- a number is a byte string, and
/// it must be the SHORTEST one that represents it, with no leading zeroes, and zero itself is the
/// EMPTY string rather than one zero byte. Encoding zero as <c>0x00</c> produces a transaction that
/// is well-formed, hashes differently, and is rejected by every node with a signature error that
/// says nothing about the cause.
/// </para>
/// </summary>
public static class Rlp
{
    private const byte SHORT_STRING_OFFSET = 0x80;
    private const byte LONG_STRING_OFFSET = 0xb7;
    private const byte SHORT_LIST_OFFSET = 0xc0;
    private const byte LONG_LIST_OFFSET = 0xf7;

    /// <summary>The longest payload a short (single-byte) length prefix can describe.</summary>
    private const int SHORT_PAYLOAD_LIMIT = 55;

    /// <summary>A byte string.</summary>
    public static byte[] Encode(ReadOnlySpan<byte> value)
    {
        // A single byte below 0x80 is its own encoding. It is the one case where the prefix would be
        // ambiguous with the value, and the specification resolves it by having no prefix at all.
        if (value.Length == 1 && value[0] < SHORT_STRING_OFFSET)
        {
            return [value[0]];
        }

        return WithPrefix(value, SHORT_STRING_OFFSET, LONG_STRING_OFFSET);
    }

    /// <summary>A list of already-encoded items.</summary>
    public static byte[] EncodeList(params byte[][] items)
    {
        int total = 0;

        foreach (byte[] item in items)
        {
            total += item.Length;
        }

        byte[] payload = new byte[total];
        int offset = 0;

        foreach (byte[] item in items)
        {
            Buffer.BlockCopy(item, 0, payload, offset, item.Length);
            offset += item.Length;
        }

        return WithPrefix(payload, SHORT_LIST_OFFSET, LONG_LIST_OFFSET);
    }

    /// <summary>
    /// A non-negative integer, as the shortest big-endian byte string that represents it -- so zero
    /// is the empty string. See the class remarks: this is the rule that is easiest to get wrong and
    /// hardest to diagnose afterwards.
    /// </summary>
    public static byte[] EncodeInteger(BigInteger value)
    {
        if (value.Sign < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "RLP has no representation for a negative integer."
            );
        }

        if (value.IsZero)
        {
            return Encode([]);
        }

        // Unsigned and big-endian, then the leading zero byte .NET adds when the top bit is set is
        // dropped -- it is exactly the leading zero RLP forbids.
        byte[] bytes = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        int start = 0;

        while (start < bytes.Length - 1 && bytes[start] == 0)
        {
            start++;
        }

        return Encode(bytes.AsSpan(start));
    }

    /// <summary>An empty list, which is what an unused EIP-2930 access list is.</summary>
    public static byte[] EncodeEmptyList() => [SHORT_LIST_OFFSET];

    private static byte[] WithPrefix(ReadOnlySpan<byte> payload, byte shortOffset, byte longOffset)
    {
        if (payload.Length <= SHORT_PAYLOAD_LIMIT)
        {
            byte[] shortForm = new byte[payload.Length + 1];

            shortForm[0] = (byte)(shortOffset + payload.Length);
            payload.CopyTo(shortForm.AsSpan(1));

            return shortForm;
        }

        // Over 55 bytes the prefix carries the LENGTH OF THE LENGTH, and the length follows it as
        // its own minimal big-endian byte string.
        List<byte> lengthBytes = [];

        for (int remaining = payload.Length; remaining > 0; remaining >>= 8)
        {
            lengthBytes.Insert(0, (byte)(remaining & 0xFF));
        }

        byte[] longForm = new byte[1 + lengthBytes.Count + payload.Length];

        longForm[0] = (byte)(longOffset + lengthBytes.Count);
        lengthBytes.CopyTo(longForm, 1);
        payload.CopyTo(longForm.AsSpan(1 + lengthBytes.Count));

        return longForm;
    }
}
