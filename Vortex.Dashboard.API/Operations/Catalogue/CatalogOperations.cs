using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;
using Vortex.Dashboard.API.Operations.Catalogue.Contracts;
using Vortex.Observability.Diagnostics;
using Vortex.Primitives.Action;
using Vortex.Primitives.Catalog;
using Vortex.Primitives.Catalog.Admin;
using Vortex.Primitives.Catalog.Snapshots;
using Vortex.Primitives.Moderation;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Enums.Wallet;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Snapshots.Avatars;

namespace Vortex.Dashboard.API.Operations.Catalogue;

/// <summary>
/// Creating, changing and removing catalogue pages, offers and products.
/// </summary>
/// <remarks>
/// Two dependencies where the service it left took thirty-two: the runner that audits every write,
/// and the domain's own catalogue authoring service. That is the whole of what writing a catalogue
/// needs, and it is now readable in one constructor.
/// </remarks>
internal sealed class CatalogOperations(OperationRunner runner, ICatalogAdminService catalogAdmin)
{
    private readonly OperationRunner _runner = runner;
    private readonly ICatalogAdminService _catalogAdmin = catalogAdmin;

    public Task<OperationResult> CreateCatalogPageAsync(
        CreateCatalogPageRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.catalog.page.create",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new
            {
                request.CatalogType,
                request.ParentId,
                request.Localization,
                request.Layout,
            },
            work: async c =>
            {
                CatalogAdminResult result = await _catalogAdmin
                    .CreatePageAsync(
                        new CatalogPageCreateSpec(
                            request.CatalogType,
                            request.ParentId,
                            request.Localization,
                            request.Name,
                            request.Icon,
                            CatalogPageLayoutExtensions.FromLayoutString(request.Layout),
                            request.ImageData,
                            request.TextData,
                            request.SortOrder,
                            request.Visible
                        ),
                        c
                    )
                    .ConfigureAwait(false);

                if (!result.Success)
                {
                    throw new InvalidOperationException(result.ErrorCode);
                }
            },
            ct
        );

    public Task<OperationResult> UpdateCatalogPageAsync(
        UpdateCatalogPageRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.catalog.page.update",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new
            {
                request.PageId,
                request.ParentId,
                request.Localization,
                request.Layout,
            },
            work: async c =>
            {
                CatalogAdminResult result = await _catalogAdmin
                    .UpdatePageAsync(
                        request.PageId,
                        new CatalogPageUpdateSpec(
                            request.ParentId,
                            request.Localization,
                            request.Name,
                            request.Icon,
                            CatalogPageLayoutExtensions.FromLayoutString(request.Layout),
                            request.ImageData,
                            request.TextData,
                            request.SortOrder,
                            request.Visible
                        ),
                        c
                    )
                    .ConfigureAwait(false);

                if (!result.Success)
                {
                    throw new InvalidOperationException(result.ErrorCode);
                }
            },
            ct
        );

    public Task<OperationResult> DeleteCatalogPageAsync(
        DeleteCatalogPageRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.catalog.page.delete",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { request.PageId },
            work: async c =>
            {
                CatalogAdminResult result = await _catalogAdmin
                    .DeletePageAsync(request.PageId, c)
                    .ConfigureAwait(false);

                if (!result.Success)
                {
                    throw new InvalidOperationException(result.ErrorCode);
                }
            },
            ct
        );

    public Task<OperationResult> CreateCatalogOfferAsync(
        CreateCatalogOfferRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.catalog.offer.create",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new
            {
                request.PageId,
                request.LocalizationId,
                request.CostCredits,
                request.CostCurrency,
            },
            work: async c =>
            {
                CatalogAdminResult result = await _catalogAdmin
                    .CreateOfferAsync(
                        new CatalogOfferCreateSpec(
                            request.PageId,
                            request.LocalizationId,
                            request.CostCredits,
                            request.CostCurrency,
                            request.CurrencyTypeId,
                            request.CanGift,
                            request.CanBundle,
                            request.ClubLevel,
                            request.DiscountPercent,
                            request.Visible
                        ),
                        c
                    )
                    .ConfigureAwait(false);

                if (!result.Success)
                {
                    throw new InvalidOperationException(result.ErrorCode);
                }
            },
            ct
        );

    public Task<OperationResult> UpdateCatalogOfferAsync(
        UpdateCatalogOfferRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.catalog.offer.update",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new
            {
                request.OfferId,
                request.LocalizationId,
                request.CostCredits,
                request.CostCurrency,
            },
            work: async c =>
            {
                CatalogAdminResult result = await _catalogAdmin
                    .UpdateOfferAsync(
                        request.OfferId,
                        new CatalogOfferUpdateSpec(
                            request.LocalizationId,
                            request.CostCredits,
                            request.CostCurrency,
                            request.CurrencyTypeId,
                            request.CanGift,
                            request.CanBundle,
                            request.ClubLevel,
                            request.DiscountPercent,
                            request.Visible
                        ),
                        c
                    )
                    .ConfigureAwait(false);

                if (!result.Success)
                {
                    throw new InvalidOperationException(result.ErrorCode);
                }
            },
            ct
        );

    public Task<OperationResult> DeleteCatalogOfferAsync(
        DeleteCatalogOfferRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.catalog.offer.delete",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { request.OfferId },
            work: async c =>
            {
                CatalogAdminResult result = await _catalogAdmin
                    .DeleteOfferAsync(request.OfferId, c)
                    .ConfigureAwait(false);

                if (!result.Success)
                {
                    throw new InvalidOperationException(result.ErrorCode);
                }
            },
            ct
        );

    public Task<OperationResult> CreateCatalogProductAsync(
        CreateCatalogProductRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.catalog.product.create",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new
            {
                request.OfferId,
                request.ProductType,
                request.FurnitureDefinitionId,
                request.Quantity,
            },
            work: async c =>
            {
                CatalogAdminResult result = await _catalogAdmin
                    .CreateProductAsync(
                        new CatalogProductCreateSpec(
                            request.OfferId,
                            request.ProductType,
                            request.FurnitureDefinitionId,
                            request.ExtraParam,
                            request.Quantity,
                            request.UniqueSize,
                            request.UniqueRemaining
                        ),
                        c
                    )
                    .ConfigureAwait(false);

                if (!result.Success)
                {
                    throw new InvalidOperationException(result.ErrorCode);
                }
            },
            ct
        );

    public Task<OperationResult> UpdateCatalogProductAsync(
        UpdateCatalogProductRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.catalog.product.update",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new
            {
                request.ProductId,
                request.ProductType,
                request.FurnitureDefinitionId,
                request.Quantity,
            },
            work: async c =>
            {
                CatalogAdminResult result = await _catalogAdmin
                    .UpdateProductAsync(
                        request.ProductId,
                        new CatalogProductUpdateSpec(
                            request.ProductType,
                            request.FurnitureDefinitionId,
                            request.ExtraParam,
                            request.Quantity,
                            request.UniqueSize,
                            request.UniqueRemaining
                        ),
                        c
                    )
                    .ConfigureAwait(false);

                if (!result.Success)
                {
                    throw new InvalidOperationException(result.ErrorCode);
                }
            },
            ct
        );

    public Task<OperationResult> DeleteCatalogProductAsync(
        DeleteCatalogProductRequest request,
        string actor,
        CancellationToken ct
    ) =>
        _runner.ExecuteAsync(
            "ops.catalog.product.delete",
            actor,
            request.Reason,
            targetPlayerId: null,
            roomId: null,
            detail: new { request.ProductId },
            work: async c =>
            {
                CatalogAdminResult result = await _catalogAdmin
                    .DeleteProductAsync(request.ProductId, c)
                    .ConfigureAwait(false);

                if (!result.Success)
                {
                    throw new InvalidOperationException(result.ErrorCode);
                }
            },
            ct
        );
}
