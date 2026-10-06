using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;
using Vortex.Primitives.Navigator;
using Vortex.Primitives.Navigator.Enums;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Orleans.Snapshots.Navigator;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Primitives.Server.Grains;

namespace Vortex.Navigator;

public sealed class NavigatorService(
    ILogger<INavigatorService> logger,
    INavigatorProvider navigatorProvider,
    IRoomService roomService,
    IGrainFactory grainFactory,
    IVortexMetrics metrics
) : INavigatorService
{
    private readonly ILogger<INavigatorService> _logger = logger;
    private readonly INavigatorProvider _navigatorProvider = navigatorProvider;
    private readonly IRoomService _roomService = roomService;
    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly IVortexMetrics _metrics = metrics;

    public async Task<ImmutableArray<NavigatorTopLevelContextSnapshot>> GetTopLevelContextAsync() =>
        await _navigatorProvider.GetTopLevelContextsAsync().ConfigureAwait(false);

    public ImmutableArray<NavigatorFlatCategorySnapshot> GetFlatCategories() =>
        _navigatorProvider.GetFlatCategories();

    public async Task<ImmutableArray<RoomInfoSnapshot>> GetSearchResultsAsync(
        string searchCode,
        NavigatorSearchFilterType filterType,
        string filterValue,
        PlayerId playerId,
        CancellationToken ct
    )
    {
        List<RoomInfoSnapshot> rooms = await FetchRoomsAsync(
                searchCode,
                filterType,
                filterValue,
                playerId,
                await GetLimitAsync().ConfigureAwait(false),
                ct
            )
            .ConfigureAwait(false);

        if (rooms.Count == 0)
        {
            return [];
        }

        Dictionary<RoomId, RoomSummarySnapshot> activeById = await GetActiveRoomsByIdAsync()
            .ConfigureAwait(false);

        return ToSearchResults(rooms, activeById);
    }

    private async Task<Dictionary<RoomId, RoomSummarySnapshot>> GetActiveRoomsByIdAsync()
    {
        ImmutableArray<RoomSummarySnapshot> activeRooms;

        using (_metrics.MeasureRoomDirectoryCall(nameof(IRoomDirectoryGrain.GetActiveRoomsAsync)))
        {
            activeRooms = await _grainFactory
                .GetRoomDirectoryGrain()
                .GetActiveRoomsAsync()
                .ConfigureAwait(false);
        }

        return activeRooms.ToDictionary(x => x.RoomId);
    }

    /// <summary>
    /// Overlays live room state (name, owner, description, population) on the persisted row for any
    /// room that currently has an activated grain.
    /// </summary>
    /// <remarks>
    /// A <c>with</c>, deliberately: this used to rebuild the result field by field into an empty
    /// subclass, and every field the room gained afterwards was dropped here without a compiler
    /// error. That cost the guild badge once. Name only what the live grain actually overrides.
    /// </remarks>
    private static ImmutableArray<RoomInfoSnapshot> ToSearchResults(
        List<RoomInfoSnapshot> rooms,
        Dictionary<RoomId, RoomSummarySnapshot> activeById
    ) =>
        [
            .. rooms.Select(x =>
                activeById.TryGetValue(x.RoomId, out RoomSummarySnapshot? active)
                    ? x with
                    {
                        Name = active.Name,
                        OwnerId = active.OwnerId,
                        OwnerName = active.OwnerName,
                        Description = active.Description,
                        Population = active.Population,
                    }
                    // Not in the directory means no activation, which means nobody is in it. The
                    // persisted headcount is a flush behind and would otherwise show a stale crowd.
                    : x with
                    {
                        Population = 0,
                    }
            ),
        ];

    public async Task<ImmutableArray<NavigatorSearchResultBlockSnapshot>> GetSearchBlocksAsync(
        string searchCode,
        NavigatorSearchFilterType filterType,
        string filterValue,
        PlayerId playerId,
        CancellationToken ct
    )
    {
        int limit = await GetLimitAsync().ConfigureAwait(false);

        // A filter turns any view into one flat list of matches -- the player asked a question, not
        // for the tab's overview.
        if (string.IsNullOrWhiteSpace(filterValue))
        {
            ImmutableArray<NavigatorQuickLinkSnapshot> quickLinks = await ResolveQuickLinksAsync(
                    searchCode
                )
                .ConfigureAwait(false);

            if (quickLinks.Length > 0)
            {
                return await BuildOverviewBlocksAsync(quickLinks, playerId, limit, ct)
                    .ConfigureAwait(false);
            }
        }

        return
        [
            await BuildBlockAsync(
                    searchCode,
                    filterType,
                    filterValue,
                    // A single block is the end of a drill-down, so the client offers "back" rather
                    // than "show more".
                    NavigatorActionAllowedType.Back,
                    playerId,
                    limit,
                    ct
                )
                .ConfigureAwait(false),
        ];
    }

    /// <summary>
    /// The quick links configured under a top-level context, which are what its overview is made of.
    /// Empty for anything that is not a tab.
    /// </summary>
    private async Task<ImmutableArray<NavigatorQuickLinkSnapshot>> ResolveQuickLinksAsync(
        string searchCode
    )
    {
        ImmutableArray<NavigatorTopLevelContextSnapshot> contexts = await _navigatorProvider
            .GetTopLevelContextsAsync()
            .ConfigureAwait(false);

        foreach (NavigatorTopLevelContextSnapshot context in contexts)
        {
            if (
                string.Equals(context.SearchCode, searchCode, StringComparison.Ordinal)
                && context.QuickLinks.Length > 0
            )
            {
                return context.QuickLinks;
            }
        }

        return [];
    }

    /// <summary>
    /// One block per quick link, carrying that quick link's own search code, label and
    /// collapsed-by-default flag. The code matters twice over: the client collapses blocks by it and
    /// falls back to localizing it when the label is empty.
    /// </summary>
    private async Task<ImmutableArray<NavigatorSearchResultBlockSnapshot>> BuildOverviewBlocksAsync(
        ImmutableArray<NavigatorQuickLinkSnapshot> quickLinks,
        PlayerId playerId,
        int limit,
        CancellationToken ct
    )
    {
        List<NavigatorSearchResultBlockSnapshot> blocks = new(quickLinks.Length);

        foreach (NavigatorQuickLinkSnapshot quickLink in quickLinks)
        {
            NavigatorSearchResultBlockSnapshot block = await BuildBlockAsync(
                    quickLink.SearchCode,
                    NavigatorSearchFilterType.Anything,
                    quickLink.Filter,
                    // The official hotel sends 0 on every overview block. "Show more" would
                    // re-search this code with no filter, which for a filtered quick link answers
                    // with a different list than the one the player is looking at.
                    NavigatorActionAllowedType.None,
                    playerId,
                    limit,
                    ct
                )
                .ConfigureAwait(false);

            // The client shows `text` when it is non-empty and otherwise localizes the search code.
            // An operator's label therefore only reaches the screen if we put it on the wire, and a
            // quick link on a code the client has no key for renders headerless without it.
            blocks.Add(
                block with
                {
                    Text = quickLink.Localization,
                    ForceClosed = quickLink.ForceClosed,
                }
            );
        }

        return [.. blocks];
    }

    private async Task<NavigatorSearchResultBlockSnapshot> BuildBlockAsync(
        string searchCode,
        NavigatorSearchFilterType filterType,
        string filterValue,
        NavigatorActionAllowedType actionAllowed,
        PlayerId playerId,
        int limit,
        CancellationToken ct
    )
    {
        List<RoomInfoSnapshot> rooms = await FetchRoomsAsync(
                searchCode,
                filterType,
                filterValue,
                playerId,
                limit,
                ct
            )
            .ConfigureAwait(false);

        Dictionary<RoomId, RoomSummarySnapshot> activeById =
            rooms.Count == 0 ? [] : await GetActiveRoomsByIdAsync().ConfigureAwait(false);

        return new NavigatorSearchResultBlockSnapshot
        {
            SearchCode = searchCode,
            // Empty text makes the client localize the search code itself.
            Text = string.Empty,
            ActionAllowed = actionAllowed,
            Localization = string.Empty,
            ForceClosed = false,
            // Frozen: view modes are no longer remembered per player, so every block is rows.
            ViewMode = NavigatorViewModeType.Rows,
            Results = ToSearchResults(rooms, activeById),
        };
    }

    private async Task<int> GetLimitAsync() =>
        await _grainFactory
            .GetServerConfigGrain()
            .GetIntAsync(
                NavigatorConfig.SearchResultLimitKey,
                NavigatorConfig.SearchResultLimitDefault
            )
            .ConfigureAwait(false);

    private async Task<List<RoomInfoSnapshot>> FetchRoomsAsync(
        string searchCode,
        NavigatorSearchFilterType filterType,
        string filterValue,
        PlayerId playerId,
        int limit,
        CancellationToken ct
    )
    {
        NavigatorQueryType? queryType = _navigatorProvider.ResolveQueryType(searchCode);

        // A code the navigator does not serve (removed in M0, or never known) answers nothing,
        // whatever was typed into the search box. It must not fall through to listing the hotel.
        if (queryType is null)
        {
            _logger.LogDebug(
                "Navigator search code is not served: SearchCode={SearchCode}",
                searchCode
            );

            return [];
        }

        // Explicit filter overrides the code's own query
        if (
            !string.IsNullOrWhiteSpace(filterValue)
            && filterType != NavigatorSearchFilterType.Anything
        )
        {
            return filterType switch
            {
                NavigatorSearchFilterType.RoomName => await _navigatorProvider
                    .GetRoomsByNameAsync(filterValue, limit, ct)
                    .ConfigureAwait(false),
                NavigatorSearchFilterType.Owner => await _navigatorProvider
                    .GetRoomsByOwnerNameAsync(filterValue, limit, ct)
                    .ConfigureAwait(false),
                NavigatorSearchFilterType.Tag => await _navigatorProvider
                    .GetRoomsByTagAsync(filterValue, limit, ct)
                    .ConfigureAwait(false),
                NavigatorSearchFilterType.Group => await _navigatorProvider
                    .GetRoomsByGroupNameAsync(filterValue, limit, ct)
                    .ConfigureAwait(false),
                _ => await _navigatorProvider
                    .SearchRoomsAsync(filterValue, limit, ct)
                    .ConfigureAwait(false),
            };
        }

        // An unprefixed search box entry arrives as Anything + a value: it is a free-text search
        // whatever served tab it was typed on.
        if (!string.IsNullOrWhiteSpace(filterValue))
        {
            return await _navigatorProvider
                .SearchRoomsAsync(filterValue, limit, ct)
                .ConfigureAwait(false);
        }

        return queryType switch
        {
            NavigatorQueryType.MyRooms => await _navigatorProvider
                .GetRoomsByOwnerAsync(playerId, limit, ct)
                .ConfigureAwait(false),
            NavigatorQueryType.FriendsRooms => await _navigatorProvider
                .GetFriendsRoomsAsync(playerId, limit, ct)
                .ConfigureAwait(false),
            NavigatorQueryType.MyGroups => await _navigatorProvider
                .GetMyGuildBaseRoomsAsync(playerId, limit, ct)
                .ConfigureAwait(false),
            NavigatorQueryType.GuildBases => await _navigatorProvider
                .GetGuildBaseRoomsAsync(limit, ct)
                .ConfigureAwait(false),
            // A text search with nothing typed is not "every room", and a query type this build no
            // longer knows (a stale stored row) lists nothing either.
            _ => [],
        };
    }
}
