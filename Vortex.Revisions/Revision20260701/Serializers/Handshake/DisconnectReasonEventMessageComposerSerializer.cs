using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Handshake;

namespace Vortex.Revisions.Revision20260701.Serializers.Handshake;

internal class DisconnectReasonEventMessageComposerSerializer(int header)
    : AbstractSerializer<DisconnectReasonEventMessageComposer>(header)
{
    protected override void Serialize(
        IServerPacket packet,
        DisconnectReasonEventMessageComposer message
    )
    {
        // _SafeCls_3893.parse: one int, read only if present.
        packet.WriteInteger(message.Reason);
    }
}
