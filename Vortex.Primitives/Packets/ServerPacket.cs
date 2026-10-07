using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Vortex.Primitives.Packets;

public class ServerPacket(int header) : VortexPacket(header), IServerPacket
{
    public BinaryWriter Writer { get; } = new(new MemoryStream());
    public MemoryStream Stream => (MemoryStream)Writer.BaseStream;

    public int Length => (int)Writer.BaseStream.Length;

    public IServerPacket WriteByte(byte b)
    {
        Writer.Write(b);
        _logger.Append($"{{u:{b}}}");

        return this;
    }

    public IServerPacket WriteBoolean(bool b)
    {
        return WriteByte((byte)(b ? 1 : 0));
    }

    public IServerPacket WriteShort(short s)
    {
        Span<byte> b = stackalloc byte[2];
        BinaryPrimitives.WriteInt16BigEndian(b, s);

        Writer.Write(b);

        _logger.Append($"{{s:{s}}}");
        return this;
    }

    public IServerPacket WriteFloat(float f)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(b, BitConverter.SingleToInt32Bits(f));

        Writer.Write(b);
        _logger.Append($"{{f:{f}}}");

        return this;
    }

    public IServerPacket WriteDouble(double d)
    {
        Span<byte> b = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(b, BitConverter.DoubleToInt64Bits(d));

        Writer.Write(b);
        _logger.Append($"{{d:{d}}}");

        return this;
    }

    public IServerPacket WriteLong(long l)
    {
        Span<byte> b = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(b, l);

        Writer.Write(b);
        _logger.Append($"{{l:{l}}}");

        return this;
    }

    public IServerPacket WriteInteger(int i)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(b, i);

        Writer.Write(b);
        _logger.Append($"{{i:{i}}}");

        return this;
    }

    public IServerPacket WriteString(string s)
    {
        byte[] data = Encoding.UTF8.GetBytes(s ?? string.Empty);

        // The length prefix is an unsigned 16-bit count; past it the client would read the rest
        // of the string as the next fields and the whole stream after it would be garbage.
        if (data.Length > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(s),
                $"A wire string is at most {ushort.MaxValue} bytes; this one is {data.Length}."
            );
        }

        WriteShort((short)data.Length);
        Writer.Write(data);

        _logger.Append($"{{s:\"{s}\"}}");

        return this;
    }

    public IServerPacket SetWriterPosition(int position)
    {
        Stream.Position = position;

        return this;
    }

    public byte[] ToArray() => Stream.ToArray();
}
