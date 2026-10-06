using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Users;
using Vortex.Revisions.Revision20260701.Serializers.Room.Pets.Snapshots;

namespace Vortex.Revisions.Revision20260701.Serializers.Users;

internal class PetRespectNotificationEventMessageComposerSerializer(int header)
    : AbstractSerializer<PetRespectNotificationEventMessageComposer>(header)
{
    protected override void Serialize(
        IServerPacket packet,
        PetRespectNotificationEventMessageComposer message
    )
    {
        packet.WriteInteger(message.PetRespect).WriteInteger(message.PetOwnerId);

        // Pet block: id, name, then the shared figure block, then level and rarity. The client
        // closes on the rarity whatever the pet is, so it is written unconditionally -- as the -1
        // the live hotel sends for an animal, rarity being a monsterplant's, removed in M0.
        packet.WriteInteger(message.Pet.PetId).WriteString(message.Pet.Name);
        PetFigureDataSerializer.Serialize(packet, message.Pet);
        packet.WriteInteger(message.Pet.Level).WriteInteger(-1);
    }
}
