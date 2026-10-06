using System.Collections.Generic;

namespace Vortex.Dashboard.API.Api.Catalogue.Contracts;

/// <summary>The currencies an offer may be priced in.</summary>
public sealed record CatalogCurrencyList(int Count, IReadOnlyList<CatalogCurrency> Items);

/// <summary>One currency an offer may be priced in.</summary>
public sealed record CatalogCurrency(int Id, string? Name, string Type, int? ActivityPointType);
