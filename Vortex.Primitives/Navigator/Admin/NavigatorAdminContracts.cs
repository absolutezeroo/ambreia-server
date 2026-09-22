using Vortex.Primitives.Navigator.Enums;

namespace Vortex.Primitives.Navigator.Admin;

/// <summary>
/// Outcome of a navigator configuration write. Mirrors the quest admin result: the navigator admin
/// service is a plain in-process singleton, not a grain, so no Orleans attributes.
/// </summary>
public sealed record NavigatorAdminResult(bool Success, int? Id, string? ErrorCode)
{
    public static NavigatorAdminResult Ok(int id) => new(true, id, null);

    public static NavigatorAdminResult Fail(string errorCode) => new(false, null, errorCode);
}

/// <summary>
/// A navigator tab. <paramref name="SearchCode"/> is the code the client sends for the tab (and the
/// key it localizes the tab's title with), so it is not free text: an unknown code renders with the
/// raw string as its title. <paramref name="QueryType"/> is what the tab's own "everything" block
/// searches.
/// </summary>
public sealed record NavigatorContextSpec(
    string SearchCode,
    bool Visible,
    NavigatorQueryType QueryType,
    int OrderNum
);

/// <summary>
/// One block inside a tab. A tab renders one block per quick link, so this is the row that decides
/// what a player actually sees under a tab — a tab with no quick links renders empty.
/// </summary>
public sealed record NavigatorQuickLinkSpec(
    int TopLevelContextId,
    string SearchCode,
    string Filter,
    string Localization,
    NavigatorQueryType QueryType,
    int OrderNum,
    bool ForceClosed
);

/// <summary>A room category. Rooms point at these through <c>navigator_category_id</c>.</summary>
public sealed record NavigatorFlatCategorySpec(
    string Name,
    bool Visible,
    bool Automatic,
    string? AutomaticCategory,
    string? GlobalCategory,
    bool StaffOnly,
    int MinRank,
    int OrderNum
);

/// <summary>An event category, which room advertisements are filed under.</summary>
public sealed record NavigatorEventCategorySpec(string Name, bool Visible);

/// <summary>
/// One promoted-rooms group in the official view. <paramref name="Code"/> must be one the client
/// can name — it localizes <c>promotedroomcategory.&lt;code&gt;</c> with no fallback, so anything
/// else renders as the raw key. <paramref name="SearchCode"/> decides which rooms fill it.
/// </summary>
public sealed record NavigatorPromotedCategorySpec(
    string Code,
    string SearchCode,
    string Filter,
    int OrderNum,
    bool Visible
);
