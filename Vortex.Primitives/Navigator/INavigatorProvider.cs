using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Navigator.Enums;
using Vortex.Primitives.Orleans.Snapshots.Navigator;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.Players;

namespace Vortex.Primitives.Navigator;

/// <summary>
/// Read side of the navigator: every "which rooms does this search mean" query, plus the cached
/// navigator reference data (top-level contexts, quick links, flat categories).
/// </summary>
/// <remarks>
/// Every room-list method takes an explicit <c>limit</c>. None of them used to, so a search on a
/// populated hotel serialized the whole <c>rooms</c> table into one packet. Callers pass the value
/// from <see cref="NavigatorConfig"/> rather than a literal.
/// </remarks>
public interface INavigatorProvider
{
    Task<ImmutableArray<NavigatorTopLevelContextSnapshot>> GetTopLevelContextsAsync();

    NavigatorQueryType ResolveQueryType(string searchCode);

    ImmutableArray<NavigatorFlatCategorySnapshot> GetFlatCategories();

    /// <summary>The configured promoted-rooms groups, in display order. Cached with the rest of the
    /// navigator reference data; the rooms behind each one are resolved per request.</summary>
    ImmutableArray<NavigatorPromotedCategorySnapshot> GetPromotedCategories();

    /// <summary>Figures for the given players, for the avatar the client draws beside a promoted
    /// group. Missing players are simply absent from the result.</summary>
    Task<ImmutableDictionary<int, string>> GetPlayerFiguresAsync(
        IReadOnlyCollection<int> playerIds,
        CancellationToken ct = default
    );

    Task<List<RoomInfoSnapshot>> GetAllRoomsAsync(int limit, CancellationToken ct = default);

    Task<List<RoomInfoSnapshot>> GetRoomsByOwnerAsync(
        PlayerId playerId,
        int limit,
        CancellationToken ct = default
    );

    /// <summary>How many rooms the player owns. Exists so the room-limit check does not have to
    /// materialize every room just to call <c>.Count</c> on the list.</summary>
    Task<int> GetRoomCountByOwnerAsync(PlayerId playerId, CancellationToken ct = default);

    Task<List<RoomInfoSnapshot>> GetRoomsByCategoryAsync(
        int categoryId,
        int limit,
        CancellationToken ct = default
    );

    /// <summary>Rooms currently advertised under a navigator event category -- backs
    /// <see cref="NavigatorQueryType.EventCategory"/> (the <c>eventcategory__N</c> search
    /// codes).</summary>
    Task<List<RoomInfoSnapshot>> GetRoomsByEventCategoryAsync(
        int eventCategoryId,
        int limit,
        CancellationToken ct = default
    );

    Task<List<RoomInfoSnapshot>> GetRoomsByNameAsync(
        string name,
        int limit,
        CancellationToken ct = default
    );

    Task<List<RoomInfoSnapshot>> GetRoomsByOwnerNameAsync(
        string ownerName,
        int limit,
        CancellationToken ct = default
    );

    Task<List<RoomInfoSnapshot>> GetRoomsByTagAsync(
        string tag,
        int limit,
        CancellationToken ct = default
    );

    /// <summary>Guild bases whose guild name matches -- backs the client's "group:" search prefix.</summary>
    Task<List<RoomInfoSnapshot>> GetRoomsByGroupNameAsync(
        string groupName,
        int limit,
        CancellationToken ct = default
    );

    /// <summary>Free-text search across room name, owner name and tags -- backs
    /// <see cref="NavigatorQueryType.TextSearch"/> (the client's unprefixed search box).</summary>
    Task<List<RoomInfoSnapshot>> SearchRoomsAsync(
        string text,
        int limit,
        CancellationToken ct = default
    );

    /// <summary>Most frequently used Tag1/Tag2 values across non-deleted rooms, most popular
    /// first -- backs GetPopularRoomTagsMessage.</summary>
    Task<ImmutableArray<string>> GetPopularTagsAsync(int limit, CancellationToken ct = default);

    /// <summary>Rooms currently promoted (staff pick or an active room advertisement), optionally
    /// narrowed to a flat category by name -- backs ForwardToARandomPromotedRoomMessage.</summary>
    Task<List<RoomInfoSnapshot>> GetPromotedRoomsAsync(
        string? categoryName,
        CancellationToken ct = default
    );

    /// <summary>Live and maximum population per flat category -- backs the client's
    /// categories-with-visitor-count view. Categories with no rooms are absent from the
    /// result.</summary>
    Task<ImmutableDictionary<int, NavigatorCategoryVisitorCount>> GetCategoryVisitorCountsAsync(
        CancellationToken ct = default
    );

    /// <summary>The navigator_eventcats reference list -- backs GetUserEventCatsMessage.</summary>
    Task<ImmutableArray<NavigatorEventCategorySnapshot>> GetEventCategoriesAsync(
        CancellationToken ct = default
    );

    Task<List<RoomInfoSnapshot>> GetFavoriteRoomsAsync(
        PlayerId playerId,
        int limit,
        CancellationToken ct = default
    );

    /// <summary>Rooms with a currently non-expired RoomAdvertisementEntity -- backs
    /// NavigatorQueryType.RoomAds, the "sponsored rooms" navigator category.</summary>
    Task<List<RoomInfoSnapshot>> GetAdvertisedRoomsAsync(int limit, CancellationToken ct = default);

    /// <summary>Every room that is a guild base -- backs NavigatorQueryType.GuildBases, the
    /// "guild base" navigator search (GuildBaseSearchMessage).</summary>
    Task<List<RoomInfoSnapshot>> GetGuildBaseRoomsAsync(int limit, CancellationToken ct = default);

    /// <summary>Guild bases of the guilds <paramref name="playerId"/> belongs to -- backs
    /// NavigatorQueryType.MyGroups (MyGuildBasesSearchMessage).</summary>
    Task<List<RoomInfoSnapshot>> GetMyGuildBaseRoomsAsync(
        PlayerId playerId,
        int limit,
        CancellationToken ct = default
    );

    /// <summary>Rooms the player has visited, from <c>room_entry_logs</c>. Ordered by most recent
    /// visit, or by visit count when <paramref name="byFrequency"/> is set -- backs
    /// NavigatorQueryType.History and FrequentHistory.</summary>
    Task<List<RoomInfoSnapshot>> GetVisitedRoomsAsync(
        PlayerId playerId,
        bool byFrequency,
        int limit,
        CancellationToken ct = default
    );

    /// <summary>Rooms where the player holds rights, excluding the ones they own (those already
    /// have their own tab) -- backs NavigatorQueryType.WithRights.</summary>
    Task<List<RoomInfoSnapshot>> GetRoomsWithRightsAsync(
        PlayerId playerId,
        int limit,
        CancellationToken ct = default
    );

    /// <summary>Rooms owned by the player's friends -- backs NavigatorQueryType.FriendsRooms.</summary>
    Task<List<RoomInfoSnapshot>> GetFriendsRoomsAsync(
        PlayerId playerId,
        int limit,
        CancellationToken ct = default
    );

    /// <summary>Specific rooms by id, in no guaranteed order -- backs
    /// NavigatorQueryType.WithFriends, whose room set comes from live presence rather than a
    /// query.</summary>
    Task<List<RoomInfoSnapshot>> GetRoomsByIdsAsync(
        IReadOnlyCollection<int> roomIds,
        CancellationToken ct = default
    );

    /// <summary>Rooms ordered by rating -- backs NavigatorQueryType.HighestScore.</summary>
    Task<List<RoomInfoSnapshot>> GetHighestScoreRoomsAsync(
        int limit,
        CancellationToken ct = default
    );

    /// <summary>Staff-picked rooms -- backs NavigatorQueryType.StaffPicks and the official-rooms
    /// view.</summary>
    Task<List<RoomInfoSnapshot>> GetStaffPickedRoomsAsync(
        int limit,
        CancellationToken ct = default
    );

    /// <summary>Rooms suggested to the player: staff picks and well-rated rooms they neither own
    /// nor have already favourited -- backs NavigatorQueryType.Recommended.</summary>
    Task<List<RoomInfoSnapshot>> GetRecommendedRoomsAsync(
        PlayerId playerId,
        int limit,
        CancellationToken ct = default
    );

    Task ReloadAsync(CancellationToken ct = default);
}
