using System;
using System.Collections.Generic;
using Vortex.Dashboard.API.Hosting;
using Vortex.Primitives.Catalog.Enums;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Furniture.StuffData;
using Vortex.Primitives.Rooms.Enums;

namespace Vortex.Dashboard.API.Operations.Catalogue.Contracts;

/// <summary>Create a catalog page. <paramref name="CatalogType"/> is 0=Normal (see
/// <c>Vortex.Primitives.Catalog.Enums.CatalogType</c>) and cannot be changed after creation.</summary>
public sealed record CreateCatalogPageRequest(
    CatalogType CatalogType,
    int? ParentId,
    string Localization,
    string? Name,
    int Icon,
    string Layout,
    List<string>? ImageData,
    List<string>? TextData,
    int SortOrder,
    bool Visible,
    string Reason
) : IReasonedRequest;
