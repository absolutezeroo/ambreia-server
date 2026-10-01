using System;
using System.Buffers.Binary;
using System.Text;

namespace Vortex.Primitives.Packets;

public class ClientPacket(int header, ReadOnlyMemory<byte> payload)
    : VortexPacket(header),
        IClientPacket
{
    private ReadOnlyMemory<byte> _payload = payload;
    private int _pos = 0;

    public int Remaining => _payload.Length - _pos;

    /// <summary>How far into the payload the reader has got. Only interesting once something went
    /// wrong: it is where the parser and the client stopped agreeing.</summary>
    public int Position => _pos;

    /// <summary>
    /// The payload as hex, with a marker at the read position.
    /// </summary>
    /// <remarks>
    /// For diagnosing a parser against the bytes the client actually sent, which is the one thing
    /// no test and no grep can tell you. Bounded on purpose: this ends up in a log, and a packet is
    /// not always something to write down in full.
    /// </remarks>
    public string ToHexDump(int maxBytes = 128)
    {
        int length = Math.Min(_payload.Length, maxBytes);
        StringBuilder builder = new(length * 3 + 32);

        for (int i = 0; i < length; i++)
        {
            builder.Append(i == _pos ? '>' : ' ').Append(_payload.Span[i].ToString("x2"));
        }

        if (_payload.Length > length)
        {
            builder.Append(" ... (").Append(_payload.Length - length).Append(" more)");
        }

        return builder.ToString().TrimStart();
    }

    public bool End => _pos >= _payload.Length;

    public byte PopByte()
    {
        Ensure(1);
        byte b = _payload.Span[_pos++];
        return b;
    }

    public byte[] PopBytes(int count)
    {
        Ensure(count);
        byte[] arr = _payload.Span.Slice(_pos, count).ToArray();
        _pos += count;
        return arr;
    }

    public bool PopBoolean()
    {
        return PopByte() != 0;
    }

    public short PopShort()
    {
        Ensure(2);
        short v = BinaryPrimitives.ReadInt16BigEndian(_payload.Span.Slice(_pos, 2));
        _pos += 2;
        return v;
    }

    public ushort PopUShort()
    {
        Ensure(2);
        ushort v = BinaryPrimitives.ReadUInt16BigEndian(_payload.Span.Slice(_pos, 2));
        _pos += 2;
        return v;
    }

    public int PopInt()
    {
        Ensure(4);
        int v = BinaryPrimitives.ReadInt32BigEndian(_payload.Span.Slice(_pos, 4));
        _pos += 4;
        return v;
    }

    public int PopCount(int minBytesPerElement) =>
        Math.Clamp(PopInt(), 0, Remaining / Math.Max(minBytesPerElement, 1));

    public float PopFloat()
    {
        Ensure(4);
        float v = BinaryPrimitives.ReadSingleBigEndian(_payload.Span.Slice(_pos, 4));
        _pos += 4;
        return v;
    }

    public long PopLong()
    {
        Ensure(8);
        long v = BinaryPrimitives.ReadInt64BigEndian(_payload.Span.Slice(_pos, 8));
        _pos += 8;
        return v;
    }

    public string PopString(Encoding? encoding = null)
    {
        ushort len = PopUShort();
        Ensure(len);
        encoding ??= Encoding.UTF8;
        string s = encoding.GetString(_payload.Span.Slice(_pos, len));
        _pos += len;
        return s;
    }

    private void Ensure(int count)
    {
        if (_pos + count > _payload.Length)
        {
            throw new InvalidOperationException(
                $"Not enough data: need {count}, remaining {Remaining}"
            );
        }
    }
}
