using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;

namespace Vortex.Revisions;

/// <summary>
/// A dev serializer under a release revision's id. AbstractSerializer frames a packet as
/// [int32 length][int16 header][payload]; only bytes 4..5 differ between revisions, so they are
/// rewritten after the dev serializer has run and the rest is left as it wrote it.
/// </summary>
internal sealed class RenumberedSerializer(ISerializer inner, int header) : ISerializer
{
    private const int HeaderOffset = 4;

    public int Header { get; } = header;

    public IServerPacket Serialize(IComposer message)
    {
        IServerPacket packet = inner.Serialize(message);
        int end = packet.Length;

        packet.SetWriterPosition(HeaderOffset);
        packet.WriteShort((short)Header);
        packet.SetWriterPosition(end);

        if (packet is VortexPacket vortex)
        {
            vortex.Header = Header;
        }

        return packet;
    }
}
