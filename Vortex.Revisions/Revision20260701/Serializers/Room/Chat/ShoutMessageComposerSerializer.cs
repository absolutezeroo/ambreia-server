using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Room.Chat;

namespace Vortex.Revisions.Revision20260701.Serializers.Room.Chat;

internal class ShoutMessageComposerSerializer(int header)
    : AbstractSerializer<ShoutMessageComposer>(header)
{
    protected override void Serialize(IServerPacket packet, ShoutMessageComposer message)
    {
        packet
            .WriteInteger(message.ObjectId)
            .WriteString(message.Text)
            .WriteInteger((int)message.Gesture)
            // styleId: figé, retiré avec le client -- chat styles were removed (M0 bis); the
            // client still reads the field.
            .WriteInteger(0)
            .WriteInteger(message.Links.Count);

        foreach ((string one, string two, bool three) in message.Links)
        {
            packet.WriteString(one).WriteString(two).WriteBoolean(three);
        }

        packet.WriteInteger(message.TrackingId);
    }
}
