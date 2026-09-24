using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Catalog.Exceptions;
using Vortex.Messages.Registry;
using Vortex.PacketHandlers.Moderation;
using Vortex.Primitives.Action;
using Vortex.Primitives.Catalog.Grains;
using Vortex.Primitives.Catalog.Snapshots;
using Vortex.Primitives.Moderation.WordFilter;
using Vortex.Primitives.Navigator;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Protocol.Messages.Incoming.Catalog;
using Vortex.Protocol.Messages.Outgoing.Catalog;

namespace Vortex.PacketHandlers.Catalog;

public class PurchaseRoomAdMessageMessageHandler(
    IGrainFactory grainFactory,
    INavigatorProvider navigatorProvider,
    IWordFilter wordFilter
) : IMessageHandler<PurchaseRoomAdMessageMessage>
{
    /// <summary>Real Habbo badge granted on a player's first-ever room-ad purchase.</summary>
    private const string RoomAdBadgeCode = "RADZZ";

    public async ValueTask HandleAsync(
        PurchaseRoomAdMessageMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0 || message.FlatId <= 0 || string.IsNullOrEmpty(message.Name))
        {
            return;
        }

        if (await SafetyLockGuard.RefusedAsync(grainFactory, ctx, ct).ConfigureAwait(false))
        {
            return;
        }

        // Before the purchase, which debits: a refused event costs nothing. PurchaseError closes
        // the catalogue's pending purchase; the bubble says why. The description is optional on
        // this packet and stays null when absent.
        string? name = await wordFilter
            .PassAsync(ctx, message.Name, WordFilterSource.RoomEvent, ct)
            .ConfigureAwait(false);
        string? description = message.Description;
        bool refused = name is null;

        if (!refused && description is not null)
        {
            description = await wordFilter
                .PassAsync(ctx, description, WordFilterSource.RoomEvent, ct)
                .ConfigureAwait(false);
            refused = description is null;
        }

        if (name is null || refused)
        {
            await ctx.SendComposerAsync(new PurchaseErrorMessageComposer(), ct)
                .ConfigureAwait(false);
            return;
        }

        if (!navigatorProvider.GetFlatCategories().Any(c => c.Id == message.CategoryId))
        {
            await ctx.SendComposerAsync(new PurchaseErrorMessageComposer(), ct)
                .ConfigureAwait(false);
            return;
        }

        IRoomSecurity roomGrain = grainFactory.GetRoomSecurity(message.FlatId);
        ActionContext actionCtx = ActionContext.CreateForPlayer(ctx.PlayerId, message.FlatId);
        RoomControllerType controllerLevel = await roomGrain
            .GetControllerLevelAsync(actionCtx, ct)
            .ConfigureAwait(false);

        if (controllerLevel < RoomControllerType.Rights)
        {
            await ctx.SendComposerAsync(new PurchaseErrorMessageComposer(), ct)
                .ConfigureAwait(false);
            return;
        }

        try
        {
            ICatalogPurchaseGrain purchaseGrain = grainFactory.GetCatalogPurchaseGrain(
                ctx.PlayerId
            );

            CatalogOfferSnapshot offer = await purchaseGrain
                .PurchaseRoomAdAsync(
                    message.OfferId,
                    message.FlatId,
                    name,
                    description,
                    message.Extended,
                    message.CategoryId,
                    ct
                )
                .ConfigureAwait(false);

            await grainFactory
                .GetInventoryGrain(ctx.PlayerId)
                .GrantBadgeAsync(RoomAdBadgeCode, ct)
                .ConfigureAwait(false);

            await ctx.SendComposerAsync(new PurchaseOKMessageComposer { Offer = offer }, ct)
                .ConfigureAwait(false);
        }
        catch (CatalogPurchaseException ex)
        {
            if (ex.BalanceFailure is not null)
            {
                await ctx.SendComposerAsync(
                        new NotEnoughBalanceMessageComposer
                        {
                            NotEnoughCredits = ex.BalanceFailure.NotEnoughCredits,
                            NotEnoughActivityPoints = ex.BalanceFailure.NotEnoughActivityPoints,
                            ActivityPointType = ex.BalanceFailure.ActivityPointType,
                        },
                        ct
                    )
                    .ConfigureAwait(false);
                return;
            }

            await ctx.SendComposerAsync(new PurchaseErrorMessageComposer(), ct)
                .ConfigureAwait(false);
        }
    }
}
