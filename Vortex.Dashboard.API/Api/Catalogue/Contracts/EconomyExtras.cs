using System.Collections.Generic;

namespace Vortex.Dashboard.API.Api.Catalogue.Contracts;

/// <summary>
/// The corners of the economy with a table but no page of their own.
/// </summary>
/// <remarks>
/// One read for these subjects because each is meaningless alone: a rentable space without whether
/// anyone is renting it is a row an operator has to go and join by hand.
/// </remarks>
public sealed record EconomyExtras(
    EconomyExtrasTotals Totals,
    IReadOnlyList<RentableSpaceRow> RentableSpaces,
    IReadOnlyList<RentableSpaceTermRow> RentableTerms,
    IReadOnlyList<CurrencyTypeRow> Currencies
);
