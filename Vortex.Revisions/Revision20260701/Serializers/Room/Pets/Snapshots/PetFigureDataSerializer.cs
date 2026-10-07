using Vortex.Primitives.Packets;
using Vortex.Primitives.Pets.Snapshots;

namespace Vortex.Revisions.Revision20260701.Serializers.Room.Pets.Snapshots;

/// <summary>
/// The pet figure block the client reads wherever a pet has to be drawn -- figure updates, level
/// notifications, the inventory and pet packages all embed this exact shape, so it is written once
/// here rather than copied into each serializer.
/// </summary>
internal static class PetFigureDataSerializer
{
    public static void Serialize(IServerPacket packet, PetSnapshot pet)
    {
        // The emulator identifies a pet by type, breed and colour. The client asks for a palette id
        // and a breed id separately; both are the breed index here, which is what pet_skins is
        // keyed on.
        packet
            .WriteInteger(pet.Type)
            .WriteInteger(pet.Skin)
            .WriteString(pet.Color)
            .WriteInteger(pet.Skin);

        // Custom parts dress up seasonal pets. Nothing here creates them, so the count is zero
        // rather than a fabricated body.
        packet.WriteInteger(0);
    }
}
