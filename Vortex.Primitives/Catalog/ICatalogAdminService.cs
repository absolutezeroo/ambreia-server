using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Catalog.Admin;

namespace Vortex.Primitives.Catalog;

/// <summary>
/// CRUD for catalog_pages/catalog_offers/catalog_products, used by the dashboard's catalog admin
/// surface. Every write reloads the <see cref="Providers.ICatalogSnapshotProvider{TTag}"/> so the
/// live in-memory snapshot clients read from never drifts from the database.
/// </summary>
public interface ICatalogAdminService
{
    Task<CatalogAdminResult> CreatePageAsync(CatalogPageCreateSpec spec, CancellationToken ct);
    Task<CatalogAdminResult> UpdatePageAsync(
        int pageId,
        CatalogPageUpdateSpec spec,
        CancellationToken ct
    );
    Task<CatalogAdminResult> DeletePageAsync(int pageId, CancellationToken ct);

    Task<CatalogAdminResult> CreateOfferAsync(CatalogOfferCreateSpec spec, CancellationToken ct);
    Task<CatalogAdminResult> UpdateOfferAsync(
        int offerId,
        CatalogOfferUpdateSpec spec,
        CancellationToken ct
    );
    Task<CatalogAdminResult> DeleteOfferAsync(int offerId, CancellationToken ct);

    Task<CatalogAdminResult> CreateProductAsync(
        CatalogProductCreateSpec spec,
        CancellationToken ct
    );
    Task<CatalogAdminResult> UpdateProductAsync(
        int productId,
        CatalogProductUpdateSpec spec,
        CancellationToken ct
    );
    Task<CatalogAdminResult> DeleteProductAsync(int productId, CancellationToken ct);
}
