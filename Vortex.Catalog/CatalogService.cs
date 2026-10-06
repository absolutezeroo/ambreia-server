using System;
using Microsoft.Extensions.Logging;
using Vortex.Primitives.Catalog;
using Vortex.Primitives.Catalog.Enums;
using Vortex.Primitives.Catalog.Providers;
using Vortex.Primitives.Catalog.Snapshots;
using Vortex.Primitives.Catalog.Tags;

namespace Vortex.Catalog;

public sealed class CatalogService(
    ILogger<ICatalogService> logger,
    ICatalogSnapshotProvider<NormalCatalog> normalCatalogProvider
) : ICatalogService
{
    private readonly ILogger<ICatalogService> _logger = logger;
    private readonly ICatalogSnapshotProvider<NormalCatalog> _normalCatalogProvider =
        normalCatalogProvider;

    public CatalogSnapshot GetCatalogSnapshot(CatalogType catalogType)
    {
        return catalogType switch
        {
            CatalogType.Normal => _normalCatalogProvider.Current,
            _ => throw new NotSupportedException($"Catalog type {catalogType} is not supported."),
        };
    }
}
