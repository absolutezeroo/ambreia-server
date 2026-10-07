using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using Vortex.Networking.Configuration;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;

namespace Vortex.Networking.Package;

internal sealed class ClientPacketDecoder(
    int maxPacketBodyBytes = NetworkingConfig.DefaultMaxPacketBodyBytes
) : IClientPacketDecoder
{
    private readonly int _maxPacketBodyBytes = maxPacketBodyBytes;

    public IClientPacket TryRead(ref SequenceReader<byte> reader, ISessionContext ctx)
    {
        if (reader.Remaining < 4)
        {
            return null!;
        }

        Span<byte> hdr = stackalloc byte[4];
        reader.Sequence.Slice(reader.Consumed, 4).CopyTo(hdr);

        int length = BinaryPrimitives.ReadInt32BigEndian(
            ctx.CryptoIn is not null ? ctx.CryptoIn.Peek(hdr.ToArray()) : hdr
        );

        // The body opens with the two-byte header, so anything shorter is malformed -- and refused
        // here, as invalid data, rather than throwing from PopShort after the cipher has already
        // advanced (which the WebSocket path does not treat as a protocol error).
        if (length < sizeof(short) || length > _maxPacketBodyBytes)
        {
            throw new InvalidDataException(
                $"Client packet declared an invalid body length of {length} bytes "
                    + $"(max {_maxPacketBodyBytes}) for session {ctx.SessionKey}."
            );
        }

        if (reader.Remaining < (length + 4))
        {
            return null!;
        }

        byte[] unread = reader.Sequence.Slice(reader.Consumed, length + 4).ToArray();
        byte[] body = ctx.CryptoIn is not null ? ctx.CryptoIn.Process(unread) : unread;
        ClientPacket packet = new ClientPacket(-1, body);

        length = packet.PopInt();
        packet.Header = packet.PopShort();

        reader.Advance(length + 4);

        return packet;
    }
}
