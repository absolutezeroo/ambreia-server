using Vortex.Primitives.Packets;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object;
using Vortex.Protocol.Messages.Outgoing.Room.Engine;

namespace Vortex.Revisions.Revision20260701.Serializers.Room.Engine;

internal class SlideObjectBundleMessageComposerSerializer(int header)
    : AbstractSerializer<SlideObjectBundleMessageComposer>(header)
{
    protected override void Serialize(
        IServerPacket packet,
        SlideObjectBundleMessageComposer message
    )
    {
        packet
            .WriteInteger(message.FromX)
            .WriteInteger(message.FromY)
            .WriteInteger(message.ToX)
            .WriteInteger(message.ToY)
            .WriteInteger(message.FloorItemHeights.Length);

        foreach ((int objectId, Altitude prev, Altitude next) in message.FloorItemHeights)
        {
            packet.WriteInteger(objectId).WriteString(prev.ToString()).WriteString(next.ToString());
        }

        // rollerItemId: figé, retiré avec le client -- rollers were removed (M0 bis); a game slide
        // was never caused by one.
        packet.WriteInteger(0);

        if (message.Avatar is not null)
        {
            (SlideAvatarMoveType moveType, int objectId, Altitude prev, Altitude next) = message
                .Avatar
                .Value;

            packet
                .WriteInteger((int)moveType)
                .WriteInteger(objectId)
                .WriteString(prev.ToString())
                .WriteString(next.ToString());
        }
    }
}
