using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Incoming.Rpg.Items;

/// <summary>Destroys part or all of the item of a bag case (spec Objets §17, 9008).</summary>
public record RpgItemDestroyMessage : IMessageEvent
{
    public required int BagSlot { get; init; }
    public required int Quantity { get; init; }
}
