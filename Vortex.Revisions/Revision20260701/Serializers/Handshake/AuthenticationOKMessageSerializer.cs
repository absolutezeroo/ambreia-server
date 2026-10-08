using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Handshake;

namespace Vortex.Revisions.Revision20260701.Serializers.Handshake;

internal class AuthenticationOKMessageSerializer(int header)
    : AbstractSerializer<AuthenticationOKMessage>(header)
{
    protected override void Serialize(IServerPacket packet, AuthenticationOKMessage message)
    {
        packet.WriteInteger(message.AccountId);
        packet.WriteInteger(message.IdentityId);
    }
}
