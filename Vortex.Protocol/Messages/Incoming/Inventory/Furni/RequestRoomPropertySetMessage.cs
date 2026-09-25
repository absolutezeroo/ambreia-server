using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Incoming.Inventory.Furni;

/// <summary>
/// "Apply this decoration": the client sends it with the INVENTORY ITEM id of a wallpaper, floor
/// or landscape it was asked to place (AS3 FurniModel.as, HabboCatalog.itemAddedToInventory).
/// </summary>
public record RequestRoomPropertySetMessage : IMessageEvent
{
    public required int ItemId { get; init; }
}
