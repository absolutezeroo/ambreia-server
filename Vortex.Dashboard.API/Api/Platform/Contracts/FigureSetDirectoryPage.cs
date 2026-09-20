using System.Collections.Generic;

namespace Vortex.Dashboard.API.Api.Platform.Contracts;

/// <summary>
/// One page of the garment picker, plus the part types it can be narrowed to.
/// </summary>
/// <remarks>
/// <paramref name="Types"/> rides along rather than being a second endpoint: the list comes from the
/// same parse, it is twenty-three entries, and a picker that had to ask twice would show an empty
/// filter on its first frame.
/// </remarks>
public sealed record FigureSetDirectoryPage(
    int Count,
    int Total,
    int Offset,
    bool HasMore,
    IReadOnlyList<string> Types,
    IReadOnlyList<FigureSetDirectoryRow> Items
);
