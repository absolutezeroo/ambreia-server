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

    /// <summary>
    /// The query a search code means, or <c>null</c> when the navigator does not serve it -- which
    /// answers nothing, never "every room".
    /// </summary>
    NavigatorQueryType? ResolveQueryType(string searchCode);

    ImmutableArray<NavigatorFlatCategorySnapshot> GetFlatCategories();

    Task<List<RoomInfoSnapshot>> GetRoomsByOwnerAsync(
        PlayerId playerId,
        int limit,
        CancellationToken ct = default
    );

    /// <summary>How many rooms the player owns. Exists so the room-limit check does not have to
    /// materialize every room just to call <c>.Count</c> on the list.</summary>
    Task<int> GetRoomCountByOwnerAsync(PlayerId playerId, CancellationToken ct = default);

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

    /// <summary>Rooms owned by the player's friends -- backs NavigatorQueryType.FriendsRooms.</summary>
    Task<List<RoomInfoSnapshot>> GetFriendsRoomsAsync(
        PlayerId playerId,
        int limit,
        CancellationToken ct = default
    );

    Task ReloadAsync(CancellationToken ct = default);
}
