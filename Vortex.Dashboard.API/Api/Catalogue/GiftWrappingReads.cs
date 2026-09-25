using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Vortex.Dashboard.API.Api.Catalogue.Contracts;
using Vortex.Dashboard.API.Infrastructure;
using Vortex.Database.Context;
using Vortex.Database.Entities.Catalog;
using Vortex.Primitives.Catalog.Enums;
using Vortex.Primitives.Catalog.Providers;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Furniture.Snapshots;

namespace Vortex.Dashboard.API.Api.Catalogue;

/// <summary>
/// The gift dialog's choices, read for the page that edits them. The writes live in
/// <see cref="Operations.Catalogue.GiftWrappingOperations"/>.
/// </summary>
/// <remarks>
/// Each parcel is resolved against the live furniture definitions the same way the provider does, so
/// a row whose name matches nothing is flagged here rather than silently missing from the dialog.
/// </remarks>
internal sealed class GiftWrappingReads(
    IDbContextFactory<VortexDbContext> dbContextFactory,
    IFurnitureDefinitionProvider furniture,
    IGiftWrappingProvider wrappings,
    DashboardAssetUrls assetUrls
) : DashboardReads(dbContextFactory)
{
    public Task<GiftWrappingList> GiftWrappingsAsync(CancellationToken ct) =>
        QueryAsync(
            async db =>
            {
                List<CatalogGiftWrappingEntity> rows = await db
                    .CatalogGiftWrappings.AsNoTracking()
                    .OrderBy(w => w.Kind)
                    .ThenBy(w => w.SortOrder)
                    .ThenBy(w => w.Id)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                return new GiftWrappingList(
                    wrappings.Configuration.IsWrappingEnabled,
                    [.. rows.Select(ToRow)]
                );
            },
            ct
        );

    private GiftWrappingRow ToRow(CatalogGiftWrappingEntity row)
    {
        FurnitureDefinitionSnapshot? definition =
            row.Kind == GiftWrappingKind.Parcel && row.FurnitureName is not null
                ? furniture.TryGetDefinitionByName(row.FurnitureName)
                : null;

        return new GiftWrappingRow(
            row.Id,
            row.Kind.ToString(),
            row.FurnitureName,
            row.FurnitureName is null ? null : assetUrls.FurniIcon(row.FurnitureName),
            definition?.SpriteId,
            row.ArtIndex,
            row.IsDefault,
            row.SortOrder
        );
    }
}
