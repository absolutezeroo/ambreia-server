using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vortex.Database.Context;
using Vortex.Database.Entities.Navigator;
using Vortex.Database.Entities.Room;
using Vortex.Primitives.Hosting;
using Vortex.Primitives.Navigator;
using Vortex.Primitives.Navigator.Enums;
using Vortex.Primitives.Orleans.Snapshots.Navigator;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Enums;

namespace Vortex.Navigator;

public sealed class NavigatorProvider(
    IDbContextFactory<VortexDbContext> dbCtxFactory,
    ILogger<NavigatorProvider> logger
) : INavigatorProvider, IReferenceDataProvider
{
    public int LoadStage => 0;

    private readonly IDbContextFactory<VortexDbContext> _dbCtxFactory = dbCtxFactory;
    private readonly ILogger<NavigatorProvider> _logger = logger;

    private ImmutableArray<NavigatorTopLevelContextSnapshot> _topLevelContexts = [];
    private ImmutableArray<NavigatorFlatCategorySnapshot> _flatCategories = [];
    private ImmutableDictionary<string, NavigatorQueryType> _queryTypeBySearchCode =
        ImmutableDictionary<string, NavigatorQueryType>.Empty;

    public Task<ImmutableArray<NavigatorTopLevelContextSnapshot>> GetTopLevelContextsAsync() =>
        Task.FromResult(_topLevelContexts);

    /// <summary>
    /// Navigator config rows win, because repointing a search code at a different query is exactly
    /// what an operator configures them for. When a code has no row -- an unseeded hotel, or a code
    /// the client sends that nobody thought to configure -- the built-in table in
    /// <see cref="NavigatorSearchCodes"/> decides. Only a genuinely unknown code degrades to
    /// <see cref="NavigatorQueryType.AllRooms"/>.
    /// </summary>
    public NavigatorQueryType ResolveQueryType(string searchCode)
    {
        if (_queryTypeBySearchCode.TryGetValue(searchCode, out NavigatorQueryType configured))
        {
            return configured;
        }

        if (
            NavigatorSearchCodes.QueryTypeBySearchCode.TryGetValue(
                searchCode,
                out NavigatorQueryType builtIn
            )
        )
        {
            return builtIn;
        }

        // Category codes carry their id in the code itself, so there is one per row rather than a
        // fixed name to look up.
        if (
            searchCode.StartsWith(
                NavigatorSearchCodes.EventCategoryPrefix,
                StringComparison.Ordinal
            )
        )
        {
            return NavigatorQueryType.EventCategory;
        }

        return searchCode.StartsWith(
            NavigatorSearchCodes.FlatCategoryPrefix,
            StringComparison.Ordinal
        )
            ? NavigatorQueryType.ByFlatCategory
            : NavigatorQueryType.AllRooms;
    }

    public ImmutableArray<NavigatorFlatCategorySnapshot> GetFlatCategories() => _flatCategories;

    public async Task<List<RoomInfoSnapshot>> GetAllRoomsAsync(
        int limit,
        CancellationToken ct = default
    )
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        return await BuildRoomQuery(dbCtx)
            .OrderByPopularity()
            .Take(limit)
            .ToRoomInfoSnapshotsAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<List<RoomInfoSnapshot>> GetRoomsByOwnerAsync(
        PlayerId playerId,
        int limit,
        CancellationToken ct = default
    )
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        return await BuildRoomQuery(dbCtx, playerId)
            .Where(x => x.PlayerEntityId == playerId.Value)
            .OrderByPopularity()
            .Take(limit)
            .ToRoomInfoSnapshotsAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<int> GetRoomCountByOwnerAsync(
        PlayerId playerId,
        CancellationToken ct = default
    )
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        // Counted with the owner exempted: this feeds the "can I create another room" limit, and a
        // sealed room still occupies a slot.
        return await BuildRoomQuery(dbCtx, playerId)
            .CountAsync(x => x.PlayerEntityId == playerId.Value, ct)
            .ConfigureAwait(false);
    }

    public async Task<List<RoomInfoSnapshot>> GetRoomsByCategoryAsync(
        int categoryId,
        int limit,
        CancellationToken ct = default
    )
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        return await BuildRoomQuery(dbCtx)
            .Where(x => x.NavigatorCategoryEntityId == categoryId)
            .OrderByPopularity()
            .Take(limit)
            .ToRoomInfoSnapshotsAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<List<RoomInfoSnapshot>> GetAdvertisedRoomsAsync(
        int limit,
        CancellationToken ct = default
    )
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        DateTime now = DateTime.UtcNow;

        return await BuildRoomQuery(dbCtx)
            .Where(x =>
                dbCtx.RoomAdvertisements.Any(a => a.RoomEntityId == x.Id && a.ExpiresAt > now)
            )
            .OrderByPopularity()
            .Take(limit)
            .ToRoomInfoSnapshotsAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<List<RoomInfoSnapshot>> GetRoomsByEventCategoryAsync(
        int eventCategoryId,
        int limit,
        CancellationToken ct = default
    )
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        DateTime now = DateTime.UtcNow;

        return await BuildRoomQuery(dbCtx)
            .Where(x =>
                dbCtx.RoomAdvertisements.Any(a =>
                    a.RoomEntityId == x.Id && a.CategoryId == eventCategoryId && a.ExpiresAt > now
                )
            )
            .OrderByPopularity()
            .Take(limit)
            .ToRoomInfoSnapshotsAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<List<RoomInfoSnapshot>> GetRoomsByNameAsync(
        string name,
        int limit,
        CancellationToken ct = default
    )
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        string lower = name.ToLowerInvariant();

        return await BuildRoomQuery(dbCtx)
            .Where(x => x.Name.ToLower().Contains(lower))
            .OrderByPopularity()
            .Take(limit)
            .ToRoomInfoSnapshotsAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<List<RoomInfoSnapshot>> GetRoomsByOwnerNameAsync(
        string ownerName,
        int limit,
        CancellationToken ct = default
    )
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        string lower = ownerName.ToLowerInvariant();

        return await BuildRoomQuery(dbCtx)
            .Where(x => x.PlayerEntity.Name.ToLower().Contains(lower))
            .OrderByPopularity()
            .Take(limit)
            .ToRoomInfoSnapshotsAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<List<RoomInfoSnapshot>> GetRoomsByTagAsync(
        string tag,
        int limit,
        CancellationToken ct = default
    )
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        return await BuildRoomQuery(dbCtx)
            .Where(x => x.Tag1 == tag || x.Tag2 == tag)
            .OrderByPopularity()
            .Take(limit)
            .ToRoomInfoSnapshotsAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<List<RoomInfoSnapshot>> SearchRoomsAsync(
        string text,
        int limit,
        CancellationToken ct = default
    )
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        string lower = text.ToLowerInvariant();

        return await BuildRoomQuery(dbCtx)
            .Where(x =>
                x.Name.ToLower().Contains(lower)
                || x.PlayerEntity.Name.ToLower().Contains(lower)
                || (x.Tag1 != null && x.Tag1.ToLower().Contains(lower))
                || (x.Tag2 != null && x.Tag2.ToLower().Contains(lower))
            )
            .OrderByPopularity()
            .Take(limit)
            .ToRoomInfoSnapshotsAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<List<RoomInfoSnapshot>> GetRoomsByGroupNameAsync(
        string groupName,
        int limit,
        CancellationToken ct = default
    )
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        string lower = groupName.ToLowerInvariant();

        return await BuildRoomQuery(dbCtx)
            .Where(x =>
                x.GroupEntityId != null
                && x.GroupEntity!.DeletedAt == null
                && x.GroupEntity.Name.ToLower().Contains(lower)
            )
            .OrderByPopularity()
            .Take(limit)
            .ToRoomInfoSnapshotsAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<ImmutableArray<string>> GetPopularTagsAsync(
        int limit,
        CancellationToken ct = default
    )
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        List<string> tags = await BuildRoomQuery(dbCtx)
            .SelectMany(x => new[] { x.Tag1, x.Tag2 })
            .Where(t => t != null)
            .GroupBy(t => t!)
            .OrderByDescending(g => g.Count())
            .Take(limit)
            .Select(g => g.Key)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return [.. tags];
    }

    public async Task<List<RoomInfoSnapshot>> GetPromotedRoomsAsync(
        string? categoryName,
        CancellationToken ct = default
    )
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        DateTime now = DateTime.UtcNow;

        IQueryable<RoomEntity> query = BuildRoomQuery(dbCtx)
            .Where(x =>
                x.IsStaffPick
                || dbCtx.RoomAdvertisements.Any(a => a.RoomEntityId == x.Id && a.ExpiresAt > now)
            );

        if (!string.IsNullOrWhiteSpace(categoryName))
        {
            int? categoryId = _flatCategories
                .Where(c => string.Equals(c.Name, categoryName, StringComparison.OrdinalIgnoreCase))
                .Select(c => (int?)c.Id)
                .FirstOrDefault();

            if (categoryId.HasValue)
            {
                query = query.Where(x => x.NavigatorCategoryEntityId == categoryId.Value);
            }
        }

        return await query.ToRoomInfoSnapshotsAsync(ct).ConfigureAwait(false);
    }

    public async Task<
        ImmutableDictionary<int, NavigatorCategoryVisitorCount>
    > GetCategoryVisitorCountsAsync(CancellationToken ct = default)
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        List<CategoryVisitorRow> rows = await BuildRoomQuery(dbCtx)
            .Where(x => x.NavigatorCategoryEntityId != null)
            .GroupBy(x => x.NavigatorCategoryEntityId!.Value)
            .Select(g => new CategoryVisitorRow
            {
                CategoryId = g.Key,
                CurrentUserCount = g.Sum(x => x.UsersNow),
                MaxUserCount = g.Sum(x => x.PlayersMax),
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return rows.ToImmutableDictionary(
            x => x.CategoryId,
            x => new NavigatorCategoryVisitorCount(x.CurrentUserCount, x.MaxUserCount)
        );
    }

    public async Task<ImmutableArray<NavigatorEventCategorySnapshot>> GetEventCategoriesAsync(
        CancellationToken ct = default
    )
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        List<NavigatorEventCategorySnapshot> categories = await dbCtx
            .NavigatorEventCategories.AsNoTracking()
            .Where(x => x.DeletedAt == null)
            .OrderBy(x => x.Id)
            .Select(x => new NavigatorEventCategorySnapshot
            {
                Id = x.Id,
                Name = x.Name,
                Visible = x.Visible,
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return [.. categories];
    }

    public async Task<List<RoomInfoSnapshot>> GetFavoriteRoomsAsync(
        PlayerId playerId,
        int limit,
        CancellationToken ct = default
    )
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        return await BuildRoomQuery(dbCtx)
            .Where(x =>
                dbCtx.PlayerFavouriteRooms.Any(f =>
                    f.PlayerEntityId == playerId.Value && f.RoomEntityId == x.Id
                )
            )
            .OrderByPopularity()
            .Take(limit)
            .ToRoomInfoSnapshotsAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<List<RoomInfoSnapshot>> GetGuildBaseRoomsAsync(
        int limit,
        CancellationToken ct = default
    )
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        return await BuildRoomQuery(dbCtx)
            .Where(x => x.GroupEntityId != null)
            .OrderByPopularity()
            .Take(limit)
            .ToRoomInfoSnapshotsAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<List<RoomInfoSnapshot>> GetMyGuildBaseRoomsAsync(
        PlayerId playerId,
        int limit,
        CancellationToken ct = default
    )
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        return await BuildRoomQuery(dbCtx)
            .Where(x =>
                x.GroupEntityId != null
                && dbCtx.GroupMembers.Any(m =>
                    m.GroupEntityId == x.GroupEntityId
                    && m.PlayerEntityId == playerId.Value
                    && m.DeletedAt == null
                )
            )
            .OrderByPopularity()
            .Take(limit)
            .ToRoomInfoSnapshotsAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<List<RoomInfoSnapshot>> GetVisitedRoomsAsync(
        PlayerId playerId,
        bool byFrequency,
        int limit,
        CancellationToken ct = default
    )
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        // One row per visit, so collapse to one entry per room first and rank the groups. Ranking
        // the raw log instead would return the same room `limit` times for a frequent visitor.
        IQueryable<VisitedRoom> grouped = dbCtx
            .RoomEntryLogs.AsNoTracking()
            .Where(x => x.PlayerEntityId == playerId.Value && x.DeletedAt == null)
            .GroupBy(x => x.RoomEntityId)
            .Select(g => new VisitedRoom
            {
                RoomId = g.Key,
                Visits = g.Count(),
                LastVisitedAt = g.Max(x => x.CreatedAt),
            });

        List<int> roomIds = await (
            byFrequency
                ? grouped.OrderByDescending(x => x.Visits).ThenByDescending(x => x.LastVisitedAt)
                : grouped.OrderByDescending(x => x.LastVisitedAt).ThenByDescending(x => x.Visits)
        )
            .Take(limit)
            .Select(x => x.RoomId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return await LoadRoomsPreservingOrderAsync(dbCtx, roomIds, ct).ConfigureAwait(false);
    }

    public async Task<List<RoomInfoSnapshot>> GetRoomsWithRightsAsync(
        PlayerId playerId,
        int limit,
        CancellationToken ct = default
    )
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        return await BuildRoomQuery(dbCtx)
            .Where(x =>
                x.PlayerEntityId != playerId.Value
                && dbCtx.RoomRights.Any(r =>
                    r.RoomEntityId == x.Id
                    && r.PlayerEntityId == playerId.Value
                    && r.DeletedAt == null
                )
            )
            .OrderByPopularity()
            .Take(limit)
            .ToRoomInfoSnapshotsAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<List<RoomInfoSnapshot>> GetFriendsRoomsAsync(
        PlayerId playerId,
        int limit,
        CancellationToken ct = default
    )
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        // messenger_friends rows are written in both directions on accept, so the player's own
        // rows are the whole friend set -- no OR on the reverse column needed.
        return await BuildRoomQuery(dbCtx)
            .Where(x =>
                dbCtx.MessengerFriends.Any(f =>
                    f.PlayerEntityId == playerId.Value
                    && f.FriendPlayerEntityId == x.PlayerEntityId
                    && f.DeletedAt == null
                )
            )
            .OrderByPopularity()
            .Take(limit)
            .ToRoomInfoSnapshotsAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<List<RoomInfoSnapshot>> GetRoomsByIdsAsync(
        IReadOnlyCollection<int> roomIds,
        CancellationToken ct = default
    )
    {
        if (roomIds.Count == 0)
        {
            return [];
        }

        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        return await BuildRoomQuery(dbCtx)
            .Where(x => roomIds.Contains(x.Id))
            .OrderByPopularity()
            .ToRoomInfoSnapshotsAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<List<RoomInfoSnapshot>> GetHighestScoreRoomsAsync(
        int limit,
        CancellationToken ct = default
    )
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        return await BuildRoomQuery(dbCtx)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.UsersNow)
            .ThenBy(x => x.Id)
            .Take(limit)
            .ToRoomInfoSnapshotsAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<List<RoomInfoSnapshot>> GetStaffPickedRoomsAsync(
        int limit,
        CancellationToken ct = default
    )
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        return await BuildRoomQuery(dbCtx)
            .Where(x => x.IsStaffPick)
            .OrderByPopularity()
            .Take(limit)
            .ToRoomInfoSnapshotsAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<List<RoomInfoSnapshot>> GetRecommendedRoomsAsync(
        PlayerId playerId,
        int limit,
        CancellationToken ct = default
    )
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        // There is no recommendation engine and inventing one would be worse than being explicit:
        // "recommended" here means editorially picked or well rated, minus what the player already
        // knows about (their own rooms and their favourites). Staff picks rank first so a hotel
        // that curates gets its curation honoured.
        return await BuildRoomQuery(dbCtx)
            .Where(x =>
                x.PlayerEntityId != playerId.Value
                && (x.IsStaffPick || x.Score > 0)
                && !dbCtx.PlayerFavouriteRooms.Any(f =>
                    f.PlayerEntityId == playerId.Value && f.RoomEntityId == x.Id
                )
            )
            .OrderByDescending(x => x.IsStaffPick)
            .ThenByDescending(x => x.Score)
            .ThenByDescending(x => x.UsersNow)
            .ThenBy(x => x.Id)
            .Take(limit)
            .ToRoomInfoSnapshotsAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task ReloadAsync(CancellationToken ct = default)
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        List<NavigatorTopLevelContextEntity> topLevelEntities = await dbCtx
            .NavigatorTopLevelContexts.AsNoTracking()
            .Include(x => x.QuickLinks)
            .Where(x => x.Visible && x.DeletedAt == null)
            .OrderBy(x => x.OrderNum)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        _topLevelContexts =
        [
            .. topLevelEntities.Select(x => new NavigatorTopLevelContextSnapshot
            {
                SearchCode = x.SearchCode,
                QueryType = x.QueryType,
                QuickLinks =
                [
                    .. (x.QuickLinks ?? [])
                        .Where(q => q.DeletedAt == null)
                        .OrderBy(q => q.OrderNum)
                        .Select(q => new NavigatorQuickLinkSnapshot
                        {
                            Id = q.Id,
                            SearchCode = q.SearchCode,
                            Filter = q.Filter,
                            Localization = q.Localization,
                            QueryType = q.QueryType,
                            ForceClosed = q.ForceClosed,
                        }),
                ],
            }),
        ];

        // A quick link and its parent context can legitimately share a search code (the context row
        // is the "all of this" entry), so collapse duplicates instead of letting ToImmutableDictionary
        // throw and take the whole reference-data load down with it.
        _queryTypeBySearchCode = _topLevelContexts
            .SelectMany(ctx =>
                ctx.QuickLinks.Select(ql => (ql.SearchCode, ql.QueryType))
                    .Append((ctx.SearchCode, ctx.QueryType))
            )
            .GroupBy(x => x.SearchCode)
            .ToImmutableDictionary(g => g.Key, g => g.First().QueryType);

        List<NavigatorFlatCategoryEntity> flatCatEntities = await dbCtx
            .NavigatorFlatCategories.AsNoTracking()
            .Where(x => x.Visible && x.DeletedAt == null)
            .OrderBy(x => x.OrderNum)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        _flatCategories =
        [
            .. flatCatEntities.Select(x => new NavigatorFlatCategorySnapshot
            {
                Id = x.Id,
                Name = x.Name,
                MinRank = x.MinRank,
                Visible = x.Visible,
                Automatic = x.Automatic,
                AutomaticCategory = x.AutomaticCategory ?? string.Empty,
                GlobalCategory = x.GlobalCategory ?? string.Empty,
                StaffOnly = x.StaffOnly,
            }),
        ];

        _logger.LogInformation(
            "Loaded navigator snapshot: TotalTopLevelContexts={Count}, FlatCategories={FlatCount}",
            _topLevelContexts.Length,
            _flatCategories.Length
        );
    }

    /// <summary>Fetches rooms by id and restores <paramref name="orderedRoomIds"/>' order, which
    /// the <c>WHERE id IN (...)</c> round trip does not preserve. Rooms that have since been
    /// deleted simply drop out.</summary>
    private static async Task<List<RoomInfoSnapshot>> LoadRoomsPreservingOrderAsync(
        VortexDbContext dbCtx,
        List<int> orderedRoomIds,
        CancellationToken ct
    )
    {
        if (orderedRoomIds.Count == 0)
        {
            return [];
        }

        List<RoomInfoSnapshot> rooms = await BuildRoomQuery(dbCtx)
            .Where(x => orderedRoomIds.Contains(x.Id))
            .ToRoomInfoSnapshotsAsync(ct)
            .ConfigureAwait(false);

        Dictionary<int, int> rankByRoomId = new(orderedRoomIds.Count);

        for (int i = 0; i < orderedRoomIds.Count; i++)
        {
            rankByRoomId.TryAdd(orderedRoomIds[i], i);
        }

        return [.. rooms.OrderBy(x => rankByRoomId.GetValueOrDefault(x.RoomId, int.MaxValue))];
    }

    /// <summary>
    /// The base every listing in this provider is built on, which is why the Builders Club seal
    /// lives here: one predicate hides a sealed room from all twenty of them at once, instead of
    /// twenty chances to forget one.
    /// </summary>
    /// <param name="owner">
    /// The player whose own rooms must stay visible even when sealed — they are the only one who
    /// can unseal them, so hiding them from their own list would leave them with a room they
    /// cannot find. Left null by every listing that shows other people's rooms.
    /// </param>
    /// <remarks>
    /// A room is sealed when it holds Builders Club furniture while its owner's subscription has
    /// lapsed; <c>IBuildersClubService.IsRoomSealedAsync</c> answers the same question for room
    /// entry, and the two must agree. Expressed here as SQL rather than by calling that service,
    /// because a listing resolves many rooms at once and a per-row service call would be a query
    /// each.
    /// <para>
    /// ponytail: two correlated EXISTS per listed room. Fine against the indexes that exist
    /// (<c>furniture(room_id, deleted_at)</c>, subscriptions by player) and measured on nothing
    /// larger; if the navigator ever shows up in a slow-query log, the shape to reach for is a
    /// denormalised "sealed" flag on the room maintained at place/pickup and at subscription
    /// expiry.
    /// </para>
    /// </remarks>
    private static IQueryable<RoomEntity> BuildRoomQuery(
        VortexDbContext dbCtx,
        PlayerId? owner = null
    )
    {
        DateTime now = DateTime.UtcNow;
        int ownerId = owner?.Value ?? 0;

        return dbCtx
            .Rooms.AsNoTracking()
            .Where(x => x.DeletedAt == null)
            .Where(x =>
                x.PlayerEntityId == ownerId
                || !dbCtx.Furnitures.Any(f =>
                    f.RoomEntityId == x.Id && f.DeletedAt == null && f.IsBuildersClub
                )
                || dbCtx.PlayerSubscriptions.Any(s =>
                    s.PlayerEntityId == x.PlayerEntityId
                    && s.SubscriptionType == SubscriptionType.BuildersClub
                    && s.ExpiresAt > now
                )
            );
    }

    /// <summary>Projection shape for the visit-log grouping. Plain settable properties: this type
    /// only ever exists inside an EF expression tree.</summary>
    private sealed class VisitedRoom
    {
        public int RoomId { get; set; }
        public int Visits { get; set; }
        public DateTime LastVisitedAt { get; set; }
    }

    /// <summary>Projection shape for the per-category population rollup.</summary>
    private sealed class CategoryVisitorRow
    {
        public int CategoryId { get; set; }
        public int CurrentUserCount { get; set; }
        public int MaxUserCount { get; set; }
    }
}

file static class RoomQueryExtensions
{
    /// <summary>Busiest first, with a stable tiebreak so a capped result set does not reshuffle
    /// between two identical searches.</summary>
    public static IOrderedQueryable<RoomEntity> OrderByPopularity(
        this IQueryable<RoomEntity> query
    ) => query.OrderByDescending(x => x.UsersNow).ThenByDescending(x => x.Score).ThenBy(x => x.Id);

    public static Task<List<RoomInfoSnapshot>> ToRoomInfoSnapshotsAsync(
        this IQueryable<RoomEntity> query,
        CancellationToken ct
    )
    {
        // Captured once so every room in one result set agrees on which ads are still running.
        DateTime now = DateTime.UtcNow;

        return query
            .Select(x => new RoomInfoSnapshot
            {
                RoomId = x.Id,
                Name = x.Name ?? string.Empty,
                Description = x.Description ?? string.Empty,
                OwnerId = (PlayerId)x.PlayerEntityId,
                OwnerName = x.PlayerEntity != null ? x.PlayerEntity.Name : string.Empty,
                Population = x.UsersNow,
                DoorMode = x.DoorMode,
                PlayersMax = x.PlayersMax,
                TradeType = x.TradeType,
                Score = x.Score,
                Ranking = x.Ranking,
                CategoryId = x.NavigatorCategoryEntityId ?? -1,
                Tags = RoomTagMapper.ToTags(x.Tag1, x.Tag2),
                StaffPick = x.IsStaffPick,
                AllowBlocking = x.AllowBlocking,
                AllowPets = x.AllowPets,
                AllowPetsEat = x.AllowPetsEat,
                GroupId = x.GroupEntityId,
                GroupName = x.GroupEntity != null ? x.GroupEntity.Name : null,
                GroupBadge = x.GroupEntity != null ? x.GroupEntity.Badge : null,
                PaintWall = x.PaintWall ?? string.Empty,
                PaintFloor = x.PaintFloor ?? string.Empty,
                PaintLandscape = x.PaintLandscape ?? string.Empty,
                // The table is a purchase log, so "the ad" is the live row that runs longest. A
                // room usually has none and then all three fall back to empty, which is what clears
                // the room-ad bit in the serializer.
                AdName =
                    x.Advertisements!.Where(a => a.DeletedAt == null && a.ExpiresAt > now)
                        .OrderByDescending(a => a.ExpiresAt)
                        .Select(a => a.Name)
                        .FirstOrDefault()
                    ?? string.Empty,
                AdDescription =
                    x.Advertisements!.Where(a => a.DeletedAt == null && a.ExpiresAt > now)
                        .OrderByDescending(a => a.ExpiresAt)
                        .Select(a => a.Description)
                        .FirstOrDefault()
                    ?? string.Empty,
                AdExpiresAt = x.Advertisements!.Where(a => a.DeletedAt == null && a.ExpiresAt > now)
                    .OrderByDescending(a => a.ExpiresAt)
                    .Select(a => (DateTime?)a.ExpiresAt)
                    .FirstOrDefault(),
                LastUpdatedUtc = DateTime.UtcNow,
            })
            .ToListAsync(ct);
    }
}
