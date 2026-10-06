using System.Collections.Generic;

namespace Vortex.Dashboard.API.Api.Hotel.Contracts;

/// <summary>
/// What is wrong with the configuration, which the tables alone do not show.
/// </summary>
/// <param name="MissingTabs">Codes the client itself asks for that this hotel has no context for.
/// Each is a tab that answers nothing at all.</param>
/// <param name="EmptyTabs">Contexts with no quick links: a tab that opens onto nothing.</param>
/// <param name="Seeded">Whether the navigator has been configured at all.</param>
public sealed record NavigatorHealth(
    int ContextCount,
    int QuickLinkCount,
    int FlatCategoryCount,
    IReadOnlyList<string> MissingTabs,
    IReadOnlyList<NavigatorEmptyTab> EmptyTabs,
    bool Seeded
);
