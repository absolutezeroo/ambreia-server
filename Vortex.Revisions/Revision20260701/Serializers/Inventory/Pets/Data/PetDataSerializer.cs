using Vortex.Primitives.Packets;
using Vortex.Primitives.Pets.Snapshots;
using Vortex.Revisions.Revision20260701.Serializers.Room.Pets.Snapshots;

namespace Vortex.Revisions.Revision20260701.Serializers.Inventory.Pets.Data;

internal static class PetDataSerializer
{
    public static void Serialize(IServerPacket packet, PetSnapshot pet)
    {
        packet.WriteInteger(pet.PetId).WriteString(pet.Name);

        PetFigureDataSerializer.Serialize(packet, pet);

        // The client reads the rarity tier unconditionally; in a list, omitting it shifted every pet
        // after the first. Rarity was a monsterplant's, removed in M0: -1 is what the live hotel
        // sends for an animal.
        packet.WriteInteger(pet.Level).WriteInteger(-1);
    }
}
