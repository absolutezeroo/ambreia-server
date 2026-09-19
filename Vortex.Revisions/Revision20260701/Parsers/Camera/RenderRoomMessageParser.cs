using System;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Incoming.Camera;

namespace Vortex.Revisions.Revision20260701.Parsers.Camera;

/// <summary>
/// Reads the one <c>ByteArray</c> argument: an int32 length, then that many bytes.
/// </summary>
/// <remarks>
/// The length is the client's, so it is believed only as far as the packet itself goes. A declared
/// length longer than what actually arrived is a truncated or hand-built frame, and the payload is
/// taken as empty rather than read past the end of the buffer — the scene decoder then refuses it
/// like any other unusable payload. How <em>large</em> a believable length may be is the hotel's
/// policy and lives in the camera config, not here.
/// </remarks>
internal class RenderRoomMessageParser : IParser
{
    public IMessageEvent Parse(IClientPacket packet)
    {
        if (packet.Remaining < sizeof(int))
        {
            return new RenderRoomMessage { Payload = ReadOnlyMemory<byte>.Empty };
        }

        int length = packet.PopInt();

        return new RenderRoomMessage
        {
            Payload =
                length > 0 && length <= packet.Remaining
                    ? packet.PopBytes(length)
                    : ReadOnlyMemory<byte>.Empty,
        };
    }
}
