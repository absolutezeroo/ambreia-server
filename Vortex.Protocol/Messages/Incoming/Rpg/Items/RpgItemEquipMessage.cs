using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Incoming.Rpg.Items;

/// <summary>Wears the item of a bag case; the server picks its slot (spec Objets §17, 9006).</summary>
public record RpgItemEquipMessage : IMessageEvent
{
    public required int BagSlot { get; init; }
}
