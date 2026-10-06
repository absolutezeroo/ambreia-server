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
    /// <see cref="NavigatorSearchCodes"/> decides. Anything else is not served and resolves to
    /// <c>null</c>: it answers nothing, it never degrades to listing the hotel.
    /// </summary>
    public NavigatorQueryType? ResolveQueryType(string searchCode)
    {
        if (_queryTypeBySearchCode.TryGetValue(searchCode, out NavigatorQueryType configured))
        {
            return configured;
        }

        return NavigatorSearchCodes.QueryTypeBySearchCode.TryGetValue(
            searchCode,
            out NavigatorQueryType builtIn
        )
            ? builtIn
            : null;
    }

    public ImmutableArray<NavigatorFlatCategorySnapshot> GetFlatCategories() => _flatCategories;

    public async Task<List<RoomInfoSnapshot>> GetRoomsByOwnerAsync(
        PlayerId playerId,
        int limit,
        CancellationToken ct = default
    )
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        return await BuildRoomQuery(dbCtx)
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
        return await BuildRoomQuery(dbCtx)
            .CountAsync(x => x.PlayerEntityId == playerId.Value, ct)
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
        // throw and take the whole reference-data load down with it. A row whose query type is the
        // unset default, or one removed in M0 (a stored 10 for "popular", say), names nothing this
        // build serves: it is left out so the built-in table, or "not served", decides.
        _queryTypeBySearchCode = _topLevelContexts
            .SelectMany(ctx =>
                ctx.QuickLinks.Select(ql => (ql.SearchCode, ql.QueryType))
                    .Append((ctx.SearchCode, ctx.QueryType))
            )
            .Where(x => x.QueryType != NavigatorQueryType.None && Enum.IsDefined(x.QueryType))
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

    /// <summary>The base every listing in this provider is built on: the rooms not deleted.</summary>
    private static IQueryable<RoomEntity> BuildRoomQuery(VortexDbContext dbCtx) =>
        dbCtx.Rooms.AsNoTracking().Where(x => x.DeletedAt == null);
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
                AllowBlocking = x.AllowBlocking,
                AllowPets = x.AllowPets,
                AllowPetsEat = x.AllowPetsEat,
                GroupId = x.GroupEntityId,
                GroupName = x.GroupEntity != null ? x.GroupEntity.Name : null,
                GroupBadge = x.GroupEntity != null ? x.GroupEntity.Badge : null,
                PaintWall = x.PaintWall ?? string.Empty,
                PaintFloor = x.PaintFloor ?? string.Empty,
                PaintLandscape = x.PaintLandscape ?? string.Empty,
                LastUpdatedUtc = DateTime.UtcNow,
            })
            .ToListAsync(ct);
    }
}
