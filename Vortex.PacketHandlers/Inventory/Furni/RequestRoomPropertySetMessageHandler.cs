using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Action;
using Vortex.Primitives.Inventory.Grains;
using Vortex.Primitives.Inventory.Snapshots;
using Vortex.Primitives.Orleans;
using Vortex.Protocol.Messages.Incoming.Inventory.Furni;

namespace Vortex.PacketHandlers.Inventory.Furni;

/// <summary>
/// Applies a wallpaper, floor or landscape from the inventory to the room the player is in. The
/// int on the wire is the item id; this handler used to read it as a room id and only re-send that
/// room's current properties, so no decoration was ever applied.
/// </summary>
public class RequestRoomPropertySetMessageHandler(IGrainFactory grainFactory)
    : IMessageHandler<RequestRoomPropertySetMessage>
{
    public async ValueTask HandleAsync(
        RequestRoomPropertySetMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        ActionContext action = ctx.AsActionContext();

        if (ctx.PlayerId <= 0 || action.RoomId <= 0 || message.ItemId <= 0)
        {
            return;
        }

        IInventoryGrain inventory = grainFactory.GetInventoryGrain(ctx.PlayerId);

        FurnitureItemSnapshot? item = await inventory
            .GetItemSnapshotAsync(message.ItemId, ct)
            .ConfigureAwait(false);

        if (item is null)
        {
            return;
        }

        if (
            await grainFactory
                .GetRoomFurni(action.RoomId)
                .ApplyDecorationAsync(action, item, ct)
                .ConfigureAwait(false)
        )
        {
            await inventory.RemoveFurnitureAsync(item.ItemId, ct).ConfigureAwait(false);
        }
    }
}
