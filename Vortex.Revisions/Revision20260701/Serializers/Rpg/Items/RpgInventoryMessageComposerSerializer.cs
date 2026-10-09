using Vortex.Primitives.Packets;
using Vortex.Primitives.Rpg.Items;
using Vortex.Protocol.Messages.Outgoing.Rpg.Items;

namespace Vortex.Revisions.Revision20260701.Serializers.Rpg.Items;

internal class RpgInventoryMessageComposerSerializer(int header)
    : AbstractSerializer<RpgInventoryMessageComposer>(header)
{
    /// <summary>On the wire for an item that never wears out (a null durability).</summary>
    private const int NoDurability = -1;

    protected override void Serialize(IServerPacket packet, RpgInventoryMessageComposer message)
    {
        packet
            .WriteInteger(message.BagSize)
            .WriteInteger(message.MaxParcels)
            .WriteInteger(message.Items.Length);

        foreach (CharacterItemSnapshot item in message.Items)
        {
            packet
                .WriteInteger(item.ItemId)
                .WriteString(item.DefinitionId)
                .WriteInteger(item.Quantity)
                .WriteInteger(item.Durability ?? NoDurability)
                .WriteBoolean(item.Bound)
                .WriteInteger((int)item.Container)
                .WriteInteger(item.Slot);
        }
    }
}
