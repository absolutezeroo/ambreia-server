using System.Collections.Generic;

namespace Vortex.Primitives.Rpg.Content;

/// <summary>
/// A class as the site's class picker sees it (spec Personnage §8). Its name and description are
/// the text keys <c>&lt;id&gt;.name</c> and <c>&lt;id&gt;.description</c>; role and primary stat are
/// written as in the content files (<c>tank</c>, <c>strength</c>).
/// </summary>
public sealed record ClassSummary(
    string Id,
    string Role,
    int Range,
    string PrimaryStat,
    IReadOnlyList<string> Specializations
);
