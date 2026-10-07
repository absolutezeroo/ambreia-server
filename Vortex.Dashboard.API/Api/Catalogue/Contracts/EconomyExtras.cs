using System.Collections.Generic;

namespace Vortex.Dashboard.API.Api.Catalogue.Contracts;

/// <summary>
/// The corners of the economy with a table but no page of their own.
/// </summary>
public sealed record EconomyExtras(
    EconomyExtrasTotals Totals,
    IReadOnlyList<CurrencyTypeRow> Currencies
);
