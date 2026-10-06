using System;

namespace Vortex.Primitives.Catalog.Enums;

public enum CatalogType
{
    Normal = 0,
    // 1 was the Builders Club catalog, removed in M0. Not renumbered: the value is persisted.
}

public static class CatalogTypeExtensions
{
    public static string ToLegacyString(this CatalogType catalogType) =>
        catalogType switch
        {
            CatalogType.Normal => "NORMAL",
            _ => throw new ArgumentOutOfRangeException(nameof(catalogType), catalogType, null),
        };

    /// <summary>
    /// Every string reads as <see cref="CatalogType.Normal"/>, the only catalogue left. A stock
    /// client still asks for <c>BUILDERS_CLUB</c> (removed in M0); throwing there counted as a parse
    /// failure, and enough of those disconnect the client.
    /// </summary>
    public static CatalogType FromLegacyString(this string catalogType) => CatalogType.Normal;
}
