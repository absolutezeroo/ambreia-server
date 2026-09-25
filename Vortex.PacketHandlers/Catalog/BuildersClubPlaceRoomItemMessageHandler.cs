using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Catalog;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Inventory.Snapshots;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Protocol.Messages.Incoming.Catalog;

namespace Vortex.PacketHandlers.Catalog;

/// <summary>
/// Free direct-to-room placement for Builders Club subscribers -- no currency debit, gated by an
/// active subscription and the subscription's furni-count limit (checked atomically inside
/// IInventoryGrain.GrantSingleFurnitureIfUnderLimitAsync). Reuses the exact same room-security-gated
/// IRoomFurni.PlaceFloorItemAsync the normal inventory-placement path uses, so ownership/rights
/// enforcement isn't duplicated here.
/// </summary>
public class BuildersClubPlaceRoomItemMessageHandler(
    IGrainFactory grainFactory,
    ICatalogService catalogService,
    IBuildersClubService buildersClubService
) : IMessageHandler<BuildersClubPlaceRoomItemMessage>
{
    public async ValueTask HandleAsync(
        BuildersClubPlaceRoomItemMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0 || ctx.RoomId <= 0)
        {
            return;
        }

        FurnitureItemSnapshot? item = await BuildersClubPlacementHelper
            .TryGrantEligibleItemAsync(
                grainFactory,
                catalogService,
                buildersClubService,
                ctx.PlayerId,
                message.OfferId,
                message.ExtraParam,
                ProductType.Floor,
                ct
            )
            .ConfigureAwait(false);

        if (item is null)
        {
            return;
        }

        IRoomFurni roomGrain = grainFactory.GetRoomFurni(ctx.RoomId);

        bool placed = await roomGrain
            .PlaceFloorItemAsync(
                ctx.AsActionContext(),
                item,
                message.X,
                message.Y,
                (Rotation)message.Direction,
                ct
            )
            .ConfigureAwait(false);

        if (!placed)
        {
            await grainFactory
                .GetInventoryGrain(ctx.PlayerId)
                .RemoveFurnitureAsync(item.ItemId, ct)
                .ConfigureAwait(false);

            return;
        }

        await BuildersClubPlacementHelper
            .CreditPlacementAsync(grainFactory, ctx.PlayerId, ct)
            .ConfigureAwait(false);
    }
}
