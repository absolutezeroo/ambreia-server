using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Incoming.Rpg.Items;

/// <summary>Takes off the item of an equipment slot, 0 to 9 (spec Objets §17, 9007).</summary>
public record RpgItemUnequipMessage : IMessageEvent
{
    public required int EquipmentSlot { get; init; }
}
