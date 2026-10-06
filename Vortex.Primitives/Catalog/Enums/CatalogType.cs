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

    public static CatalogType FromLegacyString(this string catalogType) =>
        catalogType switch
        {
            "NORMAL" => CatalogType.Normal,
            _ => throw new ArgumentOutOfRangeException(nameof(catalogType), catalogType, null),
        };
}
