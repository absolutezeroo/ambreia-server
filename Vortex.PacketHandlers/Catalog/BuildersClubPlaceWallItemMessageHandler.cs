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

/// <summary>See BuildersClubPlaceRoomItemMessageHandler -- same free, subscription-gated,
/// limit-checked direct-to-room placement, for wall items.</summary>
public class BuildersClubPlaceWallItemMessageHandler(
    IGrainFactory grainFactory,
    ICatalogService catalogService,
    IBuildersClubService buildersClubService
) : IMessageHandler<BuildersClubPlaceWallItemMessage>
{
    public async ValueTask HandleAsync(
        BuildersClubPlaceWallItemMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (
            ctx.PlayerId <= 0
            || ctx.RoomId <= 0
            || string.IsNullOrEmpty(message.Location)
            || !WallLocationParser.TryParse(
                message.Location,
                out int x,
                out int y,
                out double z,
                out int wallOffset,
                out Rotation rot
            )
        )
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
                ProductType.Wall,
                ct
            )
            .ConfigureAwait(false);

        if (item is null)
        {
            return;
        }

        IRoomFurni roomGrain = grainFactory.GetRoomFurni(ctx.RoomId);

        bool placed = await roomGrain
            .PlaceWallItemAsync(ctx.AsActionContext(), item, x, y, z, wallOffset, rot, ct)
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
