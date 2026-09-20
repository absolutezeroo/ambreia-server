using System;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Incoming.Camera;

namespace Vortex.Revisions.Revision20260701.Parsers.Camera;

/// <summary>
/// Reads the one <c>ByteArray</c> argument, exactly as <see cref="RenderRoomMessageParser"/> does:
/// the composer it comes from is the photo composer with another header.
/// </summary>
/// <remarks>
/// The length is the client's, so it is believed only as far as the packet itself goes. A declared
/// length longer than what actually arrived is a truncated or hand-built frame, and the payload is
/// taken as empty rather than read past the end of the buffer.
/// </remarks>
internal class RenderRoomThumbnailMessageParser : IParser
{
    public IMessageEvent Parse(IClientPacket packet)
    {
        if (packet.Remaining < sizeof(int))
        {
            return new RenderRoomThumbnailMessage { Payload = ReadOnlyMemory<byte>.Empty };
        }

        int length = packet.PopInt();

        return new RenderRoomThumbnailMessage
        {
            Payload =
                length > 0 && length <= packet.Remaining
                    ? packet.PopBytes(length)
                    : ReadOnlyMemory<byte>.Empty,
        };
    }
}
