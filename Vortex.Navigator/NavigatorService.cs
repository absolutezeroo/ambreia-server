using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;
using Vortex.Primitives.FriendList.Grains;
using Vortex.Primitives.Navigator;
using Vortex.Primitives.Navigator.Enums;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Orleans.Snapshots.Navigator;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Primitives.Server.Grains;
using Vortex.Primitives.Snapshots.FriendList;
using Vortex.Primitives.Snapshots.Navigator;

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

    public async Task<ImmutableArray<NavigatorSearchResultBlockSnapshot>> GetCategoryBlocksAsync(
        CancellationToken ct
    ) =>
        await GetCategoryBlocksAsync(await GetLimitsAsync().ConfigureAwait(false), ct)
            .ConfigureAwait(false);

    private async Task<ImmutableArray<NavigatorSearchResultBlockSnapshot>> GetCategoryBlocksAsync(
        NavigatorLimits limits,
        CancellationToken ct
    )
    {
        ImmutableArray<NavigatorFlatCategorySnapshot> categories =
            _navigatorProvider.GetFlatCategories();

        if (categories.Length == 0)
        {
            return [];
        }

        // The per-category queries are independent, so issue them together rather than walking the
        // category list one round trip at a time.
        List<RoomInfoSnapshot>[] roomsPerCategory = await Task.WhenAll(
                categories.Select(cat =>
                    _navigatorProvider.GetRoomsByCategoryAsync(cat.Id, limits.Category, ct)
                )
            )
            .ConfigureAwait(false);

        Dictionary<RoomId, RoomSummarySnapshot> activeById = await GetActiveRoomsByIdAsync()
            .ConfigureAwait(false);

        List<NavigatorSearchResultBlockSnapshot> blocks = new(categories.Length);

        for (int i = 0; i < categories.Length; i++)
        {
            blocks.Add(
                new NavigatorSearchResultBlockSnapshot
                {
                    // Each category gets its own code so the client can collapse them
                    // independently and so "show more" drills into that one category. A shared
                    // "categories" code made collapsing one collapse the lot.
                    SearchCode = NavigatorSearchCodes.FlatCategoryCode(categories[i].Id),
                    Text = ToCategoryHeader(categories[i]),
                    ActionAllowed = NavigatorActionAllowedType.None,
                    Localization = string.Empty,
                    ForceClosed = false,
                    ViewMode = NavigatorViewModeType.Rows,
                    Results = ToSearchResults(roomsPerCategory[i], activeById),
                }
            );
        }

        return [.. blocks];
    }

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
                await GetLimitsAsync().ConfigureAwait(false),
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

    /// <summary>
    /// A block's header is whatever the server puts in <c>Text</c>, verbatim. The client's own flat
    /// category widget renders a category as <c>${navigator.flatcategory.global.&lt;KEY&gt;}</c>
    /// when it has a global key, so mirror that here instead of pushing the operator-facing English
    /// name at every locale.
    /// </summary>
    private static string ToCategoryHeader(NavigatorFlatCategorySnapshot category) =>
        string.IsNullOrWhiteSpace(category.GlobalCategory)
            ? category.Name
            : $"${{navigator.flatcategory.global.{category.GlobalCategory}}}";

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
    /// error. That cost the guild badge once and would have cost the room ad next. Name only what
    /// the live grain actually overrides.
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

    public async Task<ImmutableArray<OfficialRoomEntrySnapshot>> GetOfficialRoomEntriesAsync(
        CancellationToken ct
    )
    {
        int limit = await _grainFactory
            .GetServerConfigGrain()
            .GetIntAsync(
                NavigatorConfig.SearchResultLimitKey,
                NavigatorConfig.SearchResultLimitDefault
            )
            .ConfigureAwait(false);

        List<RoomInfoSnapshot> rooms = await _navigatorProvider
            .GetStaffPickedRoomsAsync(limit, ct)
            .ConfigureAwait(false);

        if (rooms.Count == 0)
        {
            return [];
        }

        Dictionary<RoomId, RoomSummarySnapshot> activeById = await GetActiveRoomsByIdAsync()
            .ConfigureAwait(false);

        // The hotel has no separate "official rooms tree" model, so the staff-pick flag is what
        // marks a room as public. Each one is emitted as a Room entry carrying its own room data,
        // which is what the client needs to open it directly from this view.
        List<OfficialRoomEntrySnapshot> entries = new(rooms.Count);

        for (int i = 0; i < rooms.Count; i++)
        {
            RoomInfoSnapshot room = rooms[i];
            RoomSummarySnapshot? active = activeById.GetValueOrDefault(room.RoomId);

            RoomInfoSnapshot live = room with
            {
                Name = active?.Name ?? room.Name,
                Description = active?.Description ?? room.Description,
                OwnerName = active?.OwnerName ?? room.OwnerName,
                Population = active?.Population ?? 0,
            };

            entries.Add(
                new OfficialRoomEntrySnapshot
                {
                    Index = i,
                    PopupCaption = live.Name,
                    PopupDescription = live.Description,
                    ShowDetails = true,
                    PictureText = string.Empty,
                    // No official-room artwork exists to point at; an empty ref makes the client
                    // skip the image rather than request a missing asset.
                    PictureRef = string.Empty,
                    FolderId = -1,
                    UserCount = live.Population,
                    Type = OfficialRoomEntryType.Room,
                    Room = live,
                }
            );
        }

        return [.. entries];
    }

    public async Task<CategoriesWithVisitorCountSnapshot> GetCategoryVisitorCountsAsync(
        CancellationToken ct
    )
    {
        ImmutableDictionary<int, NavigatorCategoryVisitorCount> counts = await _navigatorProvider
            .GetCategoryVisitorCountsAsync(ct)
            .ConfigureAwait(false);

        Dictionary<int, List<int>> byCategory = [];

        // Every visible category is reported, including the empty ones: a category the client never
        // hears about renders without a population instead of with a zero.
        foreach (NavigatorFlatCategorySnapshot category in _navigatorProvider.GetFlatCategories())
        {
            NavigatorCategoryVisitorCount count = counts.GetValueOrDefault(category.Id);

            byCategory[category.Id] = [count.CurrentUserCount, count.MaxUserCount];
        }

        return new CategoriesWithVisitorCountSnapshot(byCategory);
    }

    public async Task<ImmutableArray<NavigatorSearchResultBlockSnapshot>> GetSearchBlocksAsync(
        string searchCode,
        NavigatorSearchFilterType filterType,
        string filterValue,
        PlayerId playerId,
        CancellationToken ct
    )
    {
        NavigatorLimits limits = await GetLimitsAsync().ConfigureAwait(false);

        // A filter turns any view into one flat list of matches -- the player asked a question, not
        // for the tab's overview.
        if (string.IsNullOrWhiteSpace(filterValue))
        {
            if (searchCode == NavigatorSearchCodes.Categories)
            {
                return await GetCategoryBlocksAsync(limits, ct).ConfigureAwait(false);
            }

            ImmutableArray<NavigatorQuickLinkSnapshot> quickLinks = await ResolveQuickLinksAsync(
                    searchCode
                )
                .ConfigureAwait(false);

            if (quickLinks.Length > 0)
            {
                return await BuildOverviewBlocksAsync(quickLinks, playerId, limits, ct)
                    .ConfigureAwait(false);
            }
        }

        return
        [
            await BuildBlockAsync(
                    searchCode,
                    searchCode,
                    filterType,
                    filterValue,
                    // A single block is the end of a drill-down, so the client offers "back" rather
                    // than "show more".
                    NavigatorActionAllowedType.Back,
                    playerId,
                    limits,
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
        NavigatorLimits limits,
        CancellationToken ct
    )
    {
        List<NavigatorSearchResultBlockSnapshot> blocks = new(quickLinks.Length);

        foreach (NavigatorQuickLinkSnapshot quickLink in quickLinks)
        {
            // "All categories" is itself a set of blocks, so it expands in place instead of
            // collapsing into one nameless list.
            if (
                quickLink.SearchCode == NavigatorSearchCodes.Categories
                && string.IsNullOrWhiteSpace(quickLink.Filter)
            )
            {
                blocks.AddRange(await GetCategoryBlocksAsync(limits, ct).ConfigureAwait(false));
                continue;
            }

            NavigatorSearchResultBlockSnapshot block = await BuildBlockAsync(
                    quickLink.SearchCode,
                    quickLink.SearchCode,
                    NavigatorSearchFilterType.Anything,
                    quickLink.Filter,
                    // The official hotel sends 0 on every overview block. "Show more" would
                    // re-search this code with no filter, which for a filtered quick link answers
                    // with a different list than the one the player is looking at.
                    NavigatorActionAllowedType.None,
                    playerId,
                    limits,
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
        string viewModeKey,
        NavigatorSearchFilterType filterType,
        string filterValue,
        NavigatorActionAllowedType actionAllowed,
        PlayerId playerId,
        NavigatorLimits limits,
        CancellationToken ct
    )
    {
        List<RoomInfoSnapshot> rooms = await FetchRoomsAsync(
                searchCode,
                filterType,
                filterValue,
                playerId,
                limits,
                ct
            )
            .ConfigureAwait(false);

        int viewMode = await _grainFactory
            .GetPlayerNavigatorGrain(playerId)
            .GetViewModeAsync(viewModeKey, ct)
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
            ViewMode = (NavigatorViewModeType)viewMode,
            Results = ToSearchResults(rooms, activeById),
        };
    }

    private async Task<NavigatorLimits> GetLimitsAsync()
    {
        IServerConfigGrain config = _grainFactory.GetServerConfigGrain();

        // Read once per request rather than once per block: an overview builds up to a dozen blocks
        // and each was re-asking the config grain for the same three numbers.
        return new NavigatorLimits(
            await config
                .GetIntAsync(
                    NavigatorConfig.SearchResultLimitKey,
                    NavigatorConfig.SearchResultLimitDefault
                )
                .ConfigureAwait(false),
            await config
                .GetIntAsync(
                    NavigatorConfig.CategoryResultLimitKey,
                    NavigatorConfig.CategoryResultLimitDefault
                )
                .ConfigureAwait(false),
            await config
                .GetIntAsync(NavigatorConfig.HistoryLimitKey, NavigatorConfig.HistoryLimitDefault)
                .ConfigureAwait(false)
        );
    }

    private async Task<List<RoomInfoSnapshot>> FetchRoomsAsync(
        string searchCode,
        NavigatorSearchFilterType filterType,
        string filterValue,
        PlayerId playerId,
        NavigatorLimits limits,
        CancellationToken ct
    )
    {
        int limit = limits.Search;

        // Explicit filter overrides searchCode routing
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

        NavigatorQueryType queryType = _navigatorProvider.ResolveQueryType(searchCode);

        // An unprefixed search box entry arrives as Anything + a value: it is a free-text search
        // whatever tab it was typed on.
        if (
            !string.IsNullOrWhiteSpace(filterValue)
            && queryType != NavigatorQueryType.ByFlatCategory
        )
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
            NavigatorQueryType.MyFavorites => await _navigatorProvider
                .GetFavoriteRoomsAsync(playerId, limit, ct)
                .ConfigureAwait(false),
            NavigatorQueryType.FriendsRooms => await _navigatorProvider
                .GetFriendsRoomsAsync(playerId, limit, ct)
                .ConfigureAwait(false),
            NavigatorQueryType.WithFriends => await GetRoomsWithFriendsAsync(playerId, ct)
                .ConfigureAwait(false),
            NavigatorQueryType.History => await _navigatorProvider
                .GetVisitedRoomsAsync(playerId, false, limits.History, ct)
                .ConfigureAwait(false),
            NavigatorQueryType.FrequentHistory => await _navigatorProvider
                .GetVisitedRoomsAsync(playerId, true, limits.History, ct)
                .ConfigureAwait(false),
            NavigatorQueryType.WithRights => await _navigatorProvider
                .GetRoomsWithRightsAsync(playerId, limit, ct)
                .ConfigureAwait(false),
            NavigatorQueryType.RoomAds => await _navigatorProvider
                .GetAdvertisedRoomsAsync(limit, ct)
                .ConfigureAwait(false),
            NavigatorQueryType.ByFlatCategory => await GetCategoryRoomsAsync(
                    searchCode,
                    filterValue,
                    limit,
                    ct
                )
                .ConfigureAwait(false),
            NavigatorQueryType.EventCategory => await GetEventCategoryRoomsAsync(
                    searchCode,
                    limit,
                    ct
                )
                .ConfigureAwait(false),
            // "Popular" is the busiest-first ordering the default query already applies.
            NavigatorQueryType.Popular => await _navigatorProvider
                .GetAllRoomsAsync(limit, ct)
                .ConfigureAwait(false),
            NavigatorQueryType.HighestScore => await _navigatorProvider
                .GetHighestScoreRoomsAsync(limit, ct)
                .ConfigureAwait(false),
            NavigatorQueryType.Recommended => await _navigatorProvider
                .GetRecommendedRoomsAsync(playerId, limit, ct)
                .ConfigureAwait(false),
            NavigatorQueryType.StaffPicks => await _navigatorProvider
                .GetStaffPickedRoomsAsync(limit, ct)
                .ConfigureAwait(false),
            NavigatorQueryType.TextSearch => string.IsNullOrWhiteSpace(filterValue)
                ? []
                : await _navigatorProvider
                    .SearchRoomsAsync(filterValue, limit, ct)
                    .ConfigureAwait(false),
            NavigatorQueryType.MyGroups => await _navigatorProvider
                .GetMyGuildBaseRoomsAsync(playerId, limit, ct)
                .ConfigureAwait(false),
            NavigatorQueryType.GuildBases => await _navigatorProvider
                .GetGuildBaseRoomsAsync(limit, ct)
                .ConfigureAwait(false),
            // No competition subsystem exists. Empty is the honest answer; the fallback below would
            // present the entire hotel as competition entrants.
            NavigatorQueryType.Competition => [],
            _ => await _navigatorProvider.GetAllRoomsAsync(limit, ct).ConfigureAwait(false),
        };
    }

    /// <summary>
    /// "Rooms where my friends are" is live state, not a query: the only place a player's current
    /// room is recorded is their presence grain. Ask every online friend's presence grain at once
    /// and turn the distinct room ids into room rows.
    /// </summary>
    private async Task<List<RoomInfoSnapshot>> GetRoomsWithFriendsAsync(
        PlayerId playerId,
        CancellationToken ct
    )
    {
        List<MessengerFriendSnapshot> friends = await _grainFactory
            .GetMessengerGrain(playerId)
            .GetFriendsAsync(ct)
            .ConfigureAwait(false);

        List<PlayerId> onlineFriendIds = [.. friends.Where(f => f.Online).Select(f => f.PlayerId)];

        if (onlineFriendIds.Count == 0)
        {
            return [];
        }

        RoomPointerSnapshot[] activeRooms = await Task.WhenAll(
                onlineFriendIds.Select(id =>
                    _grainFactory.GetPlayerPresenceGrain(id).GetActiveRoomAsync()
                )
            )
            .ConfigureAwait(false);

        HashSet<int> roomIds =
        [
            .. activeRooms.Where(r => r.RoomId > 0).Select(r => r.RoomId.Value),
        ];

        if (roomIds.Count == 0)
        {
            return [];
        }

        return await _navigatorProvider.GetRoomsByIdsAsync(roomIds, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// <c>eventcategory__3</c> means "rooms currently advertised under event category 3" -- the id
    /// travels in the search code itself, so there is one code per <c>navigator_eventcats</c> row.
    /// </summary>
    private async Task<List<RoomInfoSnapshot>> GetEventCategoryRoomsAsync(
        string searchCode,
        int limit,
        CancellationToken ct
    )
    {
        // Normally "eventcategory__3", but an operator can point any code at this query type, so
        // fall back to "whatever follows the last separator".
        string idPart = searchCode.StartsWith(
            NavigatorSearchCodes.EventCategoryPrefix,
            StringComparison.Ordinal
        )
            ? searchCode[NavigatorSearchCodes.EventCategoryPrefix.Length..]
            : searchCode[(searchCode.LastIndexOfAny(['_', ':']) + 1)..];

        if (
            !int.TryParse(
                idPart,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int eventCategoryId
            )
        )
        {
            _logger.LogDebug(
                "Navigator event-category search carried no usable id: SearchCode={SearchCode}",
                searchCode
            );

            return [];
        }

        return await _navigatorProvider
            .GetRoomsByEventCategoryAsync(eventCategoryId, limit, ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves which flat category a category search means. This used to be hardcoded to id 0, so
    /// every category quick link returned the same (usually empty) block regardless of which
    /// category the player clicked.
    /// </summary>
    private async Task<List<RoomInfoSnapshot>> GetCategoryRoomsAsync(
        string searchCode,
        string filterValue,
        int limit,
        CancellationToken ct
    )
    {
        int? categoryId = ResolveFlatCategoryId(searchCode, filterValue);

        if (categoryId is null)
        {
            _logger.LogDebug(
                "Navigator category search resolved no category: SearchCode={SearchCode}, Filter={Filter}",
                searchCode,
                filterValue
            );

            return [];
        }

        return await _navigatorProvider
            .GetRoomsByCategoryAsync(categoryId.Value, limit, ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// A category is addressed either by id or by name, and it can arrive in the filtering data
    /// (<c>category:3</c>) or baked into the search code of a configured quick link
    /// (<c>category_3</c>, or simply the category's own name).
    /// </summary>
    private int? ResolveFlatCategoryId(string searchCode, string filterValue)
    {
        ImmutableArray<NavigatorFlatCategorySnapshot> categories =
            _navigatorProvider.GetFlatCategories();

        if (categories.Length == 0)
        {
            return null;
        }

        foreach (string candidate in EnumerateCategoryTokens(searchCode, filterValue))
        {
            if (
                int.TryParse(
                    candidate,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int id
                )
            )
            {
                if (categories.Any(c => c.Id == id))
                {
                    return id;
                }

                continue;
            }

            foreach (NavigatorFlatCategorySnapshot category in categories)
            {
                if (string.Equals(category.Name, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return category.Id;
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateCategoryTokens(
        string searchCode,
        string filterValue
    )
    {
        if (!string.IsNullOrWhiteSpace(filterValue))
        {
            yield return filterValue.Trim();
        }

        if (string.IsNullOrWhiteSpace(searchCode))
        {
            yield break;
        }

        yield return searchCode.Trim();

        int separator = searchCode.LastIndexOfAny(['_', ':']);

        if (separator >= 0 && separator < searchCode.Length - 1)
        {
            yield return searchCode[(separator + 1)..];
        }
    }

    /// <summary>The three result caps, resolved once per request instead of once per block.</summary>
    private sealed record NavigatorLimits(int Search, int Category, int History);
}
