using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Primitives.Catalog;
using Vortex.Primitives.Catalog.Enums;
using Vortex.Primitives.Catalog.Snapshots;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Inventory.Snapshots;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Progression.Achievements;

namespace Vortex.PacketHandlers.Catalog;

/// <summary>
/// Shared "is this player allowed to place this item for free right now" + item-creation logic for
/// BuildersClubPlaceRoomItemMessageHandler/BuildersClubPlaceWallItemMessageHandler. No currency is
/// ever debited here -- Builders Club placement is a subscription perk, not a purchase.
/// </summary>
internal static class BuildersClubPlacementHelper
{
    public static async Task<FurnitureItemSnapshot?> TryGrantEligibleItemAsync(
        IGrainFactory grainFactory,
        ICatalogService catalogService,
        IBuildersClubService buildersClubService,
        int playerId,
        int offerId,
        string? extraParam,
        ProductType expectedProductType,
        CancellationToken ct
    )
    {
        if (playerId <= 0 || offerId <= 0)
        {
            return null;
        }

        BuildersClubSubscriptionSnapshot subscription = await buildersClubService
            .GetSubscriptionAsync(playerId, ct)
            .ConfigureAwait(false);

        if (!subscription.IsActive)
        {
            return null;
        }

        CatalogOfferSnapshot? offer = ResolveOffer(catalogService, offerId);

        if (offer is null)
        {
            return null;
        }

        CatalogProductSnapshot? product = offer.Products.FirstOrDefault(p =>
            p.ProductType == expectedProductType && p.BuildersClubEligible
        );

        if (product is null || product.FurniDefinitionId <= 0)
        {
            return null;
        }

        return await grainFactory
            .GetInventoryGrain(playerId)
            .GrantSingleFurnitureIfUnderLimitAsync(
                product.FurniDefinitionId,
                extraParam,
                subscription.FurniLimit,
                ct
            )
            .ConfigureAwait(false);
    }

    /// <summary>"For placing %limit% Builders Club furni". Only this flow knows a placement is one:
    /// the room sees an ordinary item.</summary>
    public static Task CreditPlacementAsync(
        IGrainFactory grainFactory,
        PlayerId playerId,
        CancellationToken ct
    ) =>
        grainFactory
            .GetPlayerAchievementGrain(playerId.Value)
            .ProgressAsync(AchievementNames.RoomDecoBC, 1, ct);

    /// <summary>Builders Club items can be sold under either catalog tree -- check BuildersClub
    /// first since that's the natural browsing context for this flow, falling back to Normal.</summary>
    private static CatalogOfferSnapshot? ResolveOffer(ICatalogService catalogService, int offerId)
    {
        CatalogSnapshot bcSnapshot = catalogService.GetCatalogSnapshot(CatalogType.BuildersClub);

        if (bcSnapshot.OffersById.TryGetValue(offerId, out CatalogOfferSnapshot? bcOffer))
        {
            return bcOffer;
        }

        CatalogSnapshot normalSnapshot = catalogService.GetCatalogSnapshot(CatalogType.Normal);

        return normalSnapshot.OffersById.TryGetValue(offerId, out CatalogOfferSnapshot? normalOffer)
            ? normalOffer
            : null;
    }
}
