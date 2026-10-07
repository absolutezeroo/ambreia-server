using Vortex.Primitives.Packets;
using Vortex.Primitives.Pets.Snapshots;
using Vortex.Protocol.Messages.Outgoing.Catalog;

namespace Vortex.Revisions.Revision20260701.Serializers.Catalog;

internal class SellablePetPalettesMessageComposerSerializer(int header)
    : AbstractSerializer<SellablePetPalettesMessageComposer>(header)
{
    protected override void Serialize(
        IServerPacket packet,
        SellablePetPalettesMessageComposer message
    )
    {
        packet.WriteString(message.ProductCode).WriteInteger(message.Palettes.Length);

        foreach (PetSkinEntry entry in message.Palettes)
        {
            packet
                .WriteInteger(entry.PetType)
                .WriteInteger(entry.SkinIndex)
                .WriteInteger(entry.Color)
                .WriteBoolean(entry.Sellable)
                .WriteBoolean(entry.Rare);
        }
    }
}
