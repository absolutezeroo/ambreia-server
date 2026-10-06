using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Navigator.Enums;
using Vortex.Primitives.Orleans.Snapshots.Navigator;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.Players;

namespace Vortex.Primitives.Navigator;

public interface INavigatorService
{
    Task<ImmutableArray<NavigatorTopLevelContextSnapshot>> GetTopLevelContextAsync();

    ImmutableArray<NavigatorFlatCategorySnapshot> GetFlatCategories();

    Task<ImmutableArray<RoomInfoSnapshot>> GetSearchResultsAsync(
        string searchCode,
        NavigatorSearchFilterType filterType,
        string filterValue,
        PlayerId playerId,
        CancellationToken ct
    );

    /// <summary>
    /// The full answer to a navigator search: one block for a plain search or a drill-down, one
    /// block per quick link when the search code is a top-level context (a tab).
    /// </summary>
    /// <remarks>
    /// A tab is an overview, not a query. "My World" is my rooms *and* my friends' rooms *and*
    /// my guild bases, each as its own collapsible block — which is why the client tracks collapsed state per block search code and offers a
    /// "show more" that re-searches that one code.
    /// </remarks>
    Task<ImmutableArray<NavigatorSearchResultBlockSnapshot>> GetSearchBlocksAsync(
        string searchCode,
        NavigatorSearchFilterType filterType,
        string filterValue,
        PlayerId playerId,
        CancellationToken ct
    );
}
