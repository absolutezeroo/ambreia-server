using System.Collections.Generic;

namespace Vortex.Dashboard.API.Api.Hotel.Contracts;

/// <summary>
/// The navigator's own configuration: the tabs, what each offers, and the categories rooms sit in.
/// </summary>
/// <remarks>
/// Named NavigatorSetup rather than NavigatorConfig because Vortex.Primitives already owns that
/// name for what the client is served; this is what an operator edits.
/// </remarks>
/// <param name="SearchCodes">Every code the client knows, so the editor offers the real vocabulary
/// rather than a text box that saves a code answering nothing.</param>
public sealed record NavigatorSetup(
    NavigatorHealth Health,
    IReadOnlyList<NavigatorContextRow> Contexts,
    IReadOnlyList<NavigatorFlatCategoryRow> FlatCategories,
    IReadOnlyList<NavigatorSearchCodeOption> SearchCodes,
    IReadOnlyList<NavigatorQueryTypeOption> QueryTypes
);
