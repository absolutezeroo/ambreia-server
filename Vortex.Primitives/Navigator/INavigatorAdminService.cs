using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Navigator.Admin;

namespace Vortex.Primitives.Navigator;

/// <summary>
/// CRUD for the navigator's own configuration tables (tabs, the blocks inside them, room categories
/// and event categories), used by the dashboard's navigator admin surface.
/// <para>
/// Every write reloads <see cref="INavigatorProvider"/>'s in-memory snapshot: that snapshot is built
/// once at reference-data load and never re-read, so a committed row that skipped the reload stays
/// invisible to every player until the next restart.
/// </para>
/// </summary>
public interface INavigatorAdminService
{
    Task<NavigatorAdminResult> CreateContextAsync(NavigatorContextSpec spec, CancellationToken ct);

    Task<NavigatorAdminResult> UpdateContextAsync(
        int contextId,
        NavigatorContextSpec spec,
        CancellationToken ct
    );

    Task<NavigatorAdminResult> DeleteContextAsync(int contextId, CancellationToken ct);

    Task<NavigatorAdminResult> CreateQuickLinkAsync(
        NavigatorQuickLinkSpec spec,
        CancellationToken ct
    );

    Task<NavigatorAdminResult> UpdateQuickLinkAsync(
        int quickLinkId,
        NavigatorQuickLinkSpec spec,
        CancellationToken ct
    );

    Task<NavigatorAdminResult> DeleteQuickLinkAsync(int quickLinkId, CancellationToken ct);

    Task<NavigatorAdminResult> CreateFlatCategoryAsync(
        NavigatorFlatCategorySpec spec,
        CancellationToken ct
    );

    Task<NavigatorAdminResult> UpdateFlatCategoryAsync(
        int categoryId,
        NavigatorFlatCategorySpec spec,
        CancellationToken ct
    );

    Task<NavigatorAdminResult> DeleteFlatCategoryAsync(int categoryId, CancellationToken ct);

    Task<NavigatorAdminResult> CreateEventCategoryAsync(
        NavigatorEventCategorySpec spec,
        CancellationToken ct
    );

    Task<NavigatorAdminResult> UpdateEventCategoryAsync(
        int categoryId,
        NavigatorEventCategorySpec spec,
        CancellationToken ct
    );

    Task<NavigatorAdminResult> DeleteEventCategoryAsync(int categoryId, CancellationToken ct);

    Task<NavigatorAdminResult> CreatePromotedCategoryAsync(
        NavigatorPromotedCategorySpec spec,
        CancellationToken ct
    );

    Task<NavigatorAdminResult> UpdatePromotedCategoryAsync(
        int promotedCategoryId,
        NavigatorPromotedCategorySpec spec,
        CancellationToken ct
    );

    Task<NavigatorAdminResult> DeletePromotedCategoryAsync(
        int promotedCategoryId,
        CancellationToken ct
    );

    /// <summary>
    /// Creates the four client tabs and their standard blocks for any that are missing. An unseeded
    /// hotel answers every navigator request with an empty left pane, and the codes involved are the
    /// client's own — so this is a fill-in-the-blanks operation, not a reset: existing rows are left
    /// exactly as the operator configured them.
    /// </summary>
    Task<NavigatorAdminResult> SeedDefaultsAsync(CancellationToken ct);
}
