using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Handshake;

namespace Vortex.Revisions.Revision20260701.Serializers.Handshake;

internal class UserRightsMessageSerializer(int header)
    : AbstractSerializer<UserRightsMessage>(header)
{
    protected override void Serialize(IServerPacket packet, UserRightsMessage message)
    {
        packet.WriteInteger((int)message.SecurityLevel);
    }
}
