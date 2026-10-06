using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.Specialized;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Orleans;
using Vortex.Dashboard.API.Api.Platform.Contracts;
using Vortex.Dashboard.API.Infrastructure;
using Vortex.Database.Context;
using Vortex.Database.Entities.Audit;
using Vortex.Database.Entities.Furniture;
using Vortex.Database.Entities.Marketplace;
using Vortex.Database.Entities.Players;
using Vortex.Database.Entities.Room;
using Vortex.Observability.Configuration;
using Vortex.Observability.Metrics;
using Vortex.Observability.Runtime;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.Players.Enums;
using Vortex.Primitives.Rooms.Grains;

namespace Vortex.Dashboard.API.Api.Platform;

internal sealed class DirectoryReads(
    IDbContextFactory<VortexDbContext> dbContextFactory,
    DashboardAssetUrls assetUrls,
    ISessionGateway sessionGateway
) : DashboardReads(dbContextFactory)
{
    private readonly DashboardAssetUrls _assetUrls = assetUrls;
    private readonly ISessionGateway _sessionGateway = sessionGateway;

    public Task<ItemProfile?> ItemAsync(
        string idText,
        NameValueCollection query,
        CancellationToken ct
    )
    {
        if (!long.TryParse(idText, out long itemId))
        {
            return Task.FromResult<ItemProfile?>(null);
        }

        return QueryAsync<ItemProfile?>(
            async db =>
            {
                int limit = QueryValues.Limit(query["limit"], 50, 500);
                int page = QueryValues.Page(query["page"]);
                int offset = Math.Max(0, (page - 1) * limit);
                IQueryable<ItemEventEntity> q = db
                    .ItemEvents.AsNoTracking()
                    .Where(i => i.ItemId == itemId);

                DateTime? since = TimeWindow.ParseDateTime(query["since"]);
                DateTime? until = TimeWindow.ParseDateTime(query["until"]);

                if (since is not null)
                {
                    q = q.Where(i => i.OccurredAt >= since.Value);
                }

                if (until is not null)
                {
                    q = q.Where(i => i.OccurredAt <= until.Value);
                }

                int total = await q.CountAsync(ct).ConfigureAwait(false);

                var itemSnapshot = await db
                    .Furnitures.AsNoTracking()
                    .Where(f => f.Id == itemId)
                    .Select(f => new
                    {
                        f.Id,
                        definitionId = (int?)f.FurnitureDefinitionEntityId,
                        definitionName = f.FurnitureDefinitionEntity != null
                            ? f.FurnitureDefinitionEntity.Name
                            : null,
                        ownerPlayerId = (int?)f.PlayerEntityId,
                        ownerName = f.PlayerEntity != null ? f.PlayerEntity.Name : null,
                        roomId = (int?)f.RoomEntityId,
                        roomName = f.RoomEntity != null ? f.RoomEntity.Name : null,
                        roomX = (int?)f.X,
                        roomY = (int?)f.Y,
                        roomZ = f.Z,
                        f.ExtraData,
                        updatedAt = f.UpdatedAt,
                    })
                    .FirstOrDefaultAsync(ct)
                    .ConfigureAwait(false);

                // BuildFurniIconUrl is not translatable to SQL, so the icon is attached once the
                // row is in memory -- the same two-step the catalog projections use.
                ItemSnapshot? itemSnapshotWithIcon = itemSnapshot is null
                    ? null
                    : new ItemSnapshot(
                        itemSnapshot.Id,
                        itemSnapshot.definitionId,
                        itemSnapshot.definitionName,
                        itemSnapshot.definitionName is null
                            ? null
                            : _assetUrls.FurniIcon(itemSnapshot.definitionName),
                        itemSnapshot.ownerPlayerId,
                        itemSnapshot.ownerName,
                        itemSnapshot.roomId,
                        itemSnapshot.roomName,
                        itemSnapshot.roomX,
                        itemSnapshot.roomY,
                        itemSnapshot.roomZ,
                        itemSnapshot.ExtraData,
                        itemSnapshot.updatedAt
                    );

                var rows = await q.OrderBy(i => i.OccurredAt)
                    .Skip(offset)
                    .Take(limit)
                    .Select(i => new
                    {
                        i.Id,
                        i.OccurredAt,
                        eventType = i.EventType.ToString(),
                        i.ActorPlayerId,
                        i.FromOwnerId,
                        i.ToOwnerId,
                        i.RoomId,
                        i.CorrelationId,
                        i.Data,
                    })
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                List<int> rowPlayerIds = DisplayNameQueries.NormalizeIds(
                    rows.SelectMany(r => new[] { r.ActorPlayerId, r.FromOwnerId, r.ToOwnerId })
                );

                Dictionary<int, string> rowPlayerNames = await db.PlayerNamesAsync(rowPlayerIds, ct)
                    .ConfigureAwait(false);

                List<int> rowRoomIds = DisplayNameQueries.NormalizeIds(rows.Select(r => r.RoomId));

                Dictionary<int, string> rowRoomNames = await db.RoomNamesAsync(rowRoomIds, ct)
                    .ConfigureAwait(false);

                List<ItemEventRow> rowsWithNames = rows.Select(r => new ItemEventRow(
                        r.Id,
                        r.OccurredAt,
                        r.eventType,
                        r.ActorPlayerId,
                        DisplayNameQueries.ResolvePlayerName(rowPlayerNames, r.ActorPlayerId),
                        r.FromOwnerId,
                        DisplayNameQueries.ResolvePlayerName(rowPlayerNames, r.FromOwnerId),
                        r.ToOwnerId,
                        DisplayNameQueries.ResolvePlayerName(rowPlayerNames, r.ToOwnerId),
                        r.RoomId,
                        r.RoomId != null
                        && rowRoomNames.TryGetValue(r.RoomId.Value, out string? roomName)
                            ? roomName
                            : null,
                        r.CorrelationId,
                        r.Data
                    ))
                    .ToList();

                return new ItemProfile(
                    itemId,
                    itemSnapshotWithIcon,
                    page,
                    limit,
                    total,
                    offset,
                    rows.Count,
                    rowsWithNames
                );
            },
            ct
        );
    }

    /// <summary>
    /// The investigation search: one box that takes whatever an operator has in front of them.
    /// </summary>
    /// <remarks>
    /// The term is not asked what it is, it is recognised by its shape — thirty-two hex characters
    /// is a correlation id, a number is an id — and each shape is answered by its own method below.
    /// Nothing else is a search: an unrecognised term says so rather than guessing, because a wrong
    /// guess on an investigation screen is worse than no answer.
    /// </remarks>
    public Task<DirectorySearch> SearchAsync(NameValueCollection query, CancellationToken ct) =>
        QueryAsync<DirectorySearch>(
            async db =>
            {
                string term = (query["q"] ?? string.Empty).Trim();
                int limit = QueryValues.Limit(query["limit"], 50, 500);
                int page = QueryValues.Page(query["page"]);
                int offset = Math.Max(0, (page - 1) * limit);

                DateTime? since = TimeWindow.ParseDateTime(query["since"]);
                DateTime? until = TimeWindow.ParseDateTime(query["until"]);

                // Correlation id: 32 hex chars (Guid "N").
                if (term.Length == 32 && term.All(Uri.IsHexDigit))
                {
                    return await SearchByCorrelationAsync(
                            db,
                            term,
                            since,
                            until,
                            limit,
                            page,
                            offset,
                            ct
                        )
                        .ConfigureAwait(false);
                }

                if (int.TryParse(term, out int id))
                {
                    return await SearchByIdAsync(
                            db,
                            term,
                            id,
                            since,
                            until,
                            limit,
                            page,
                            offset,
                            ct
                        )
                        .ConfigureAwait(false);
                }

                return new UnknownSearch(
                    term,
                    "Enter a player/item id, or a 32-char correlation id."
                );
            },
            ct
        );

    /// <summary>
    /// Everything one correlation id touched: the audit trail, the money and the items, gathered
    /// from the three journals that carry it.
    /// </summary>
    /// <remarks>
    /// Chosen by the shape of the term alone, so it answers — with empty sections — even for an id
    /// that correlates to nothing. That is deliberate: "no trace of this operation" is itself the
    /// finding an investigation is often after.
    /// </remarks>
    private static async Task<CorrelationSearch> SearchByCorrelationAsync(
        VortexDbContext db,
        string term,
        DateTime? since,
        DateTime? until,
        int limit,
        int page,
        int offset,
        CancellationToken ct
    )
    {
        IQueryable<AuditEventEntity> audit = db
            .AuditEvents.AsNoTracking()
            .Where(a => a.CorrelationId == term);

        if (since is not null)
        {
            audit = audit.Where(a => a.OccurredAt >= since.Value);
        }

        if (until is not null)
        {
            audit = audit.Where(a => a.OccurredAt <= until.Value);
        }

        IQueryable<EconomyLedgerEntity> ledger = db
            .EconomyLedger.AsNoTracking()
            .Where(l => l.CorrelationId == term);

        if (since is not null)
        {
            ledger = ledger.Where(l => l.OccurredAt >= since.Value);
        }

        if (until is not null)
        {
            ledger = ledger.Where(l => l.OccurredAt <= until.Value);
        }

        IQueryable<ItemEventEntity> items = db
            .ItemEvents.AsNoTracking()
            .Where(i => i.CorrelationId == term);

        if (since is not null)
        {
            items = items.Where(i => i.OccurredAt >= since.Value);
        }

        if (until is not null)
        {
            items = items.Where(i => i.OccurredAt <= until.Value);
        }

        var auditRows = await audit
            .OrderBy(a => a.OccurredAt)
            .Skip(offset)
            .Take(limit)
            .Select(a => new
            {
                a.OccurredAt,
                category = a.Category.ToString(),
                a.Action,
                a.ActorPlayerId,
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        List<int> auditActorIds = DisplayNameQueries.NormalizeIds(
            auditRows.Select(a => a.ActorPlayerId)
        );

        Dictionary<int, string> auditActorNames = await db.PlayerNamesAsync(auditActorIds, ct)
            .ConfigureAwait(false);

        List<CorrelationAuditRow> auditRowsWithNames = auditRows
            .Select(a => new CorrelationAuditRow(
                a.OccurredAt,
                a.category,
                a.Action,
                a.ActorPlayerId,
                DisplayNameQueries.ResolvePlayerName(auditActorNames, a.ActorPlayerId)
            ))
            .ToList();

        List<CorrelationLedgerRow> ledgerRows = await ledger
            .OrderBy(l => l.OccurredAt)
            .Skip(offset)
            .Take(limit)
            .Select(l => new CorrelationLedgerRow(
                l.OccurredAt,
                l.PlayerId,
                l.Currency,
                l.Delta,
                l.BalanceAfter,
                l.ActivityPointType
            ))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        List<CorrelationItemRow> itemRows = await items
            .OrderBy(i => i.OccurredAt)
            .Skip(offset)
            .Take(limit)
            .Select(i => new CorrelationItemRow(i.OccurredAt, i.ItemId, i.EventType.ToString()))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return new CorrelationSearch(
            term,
            page,
            limit,
            offset,
            auditRowsWithNames,
            ledgerRows,
            itemRows
        );
    }

    /// <summary>
    /// One player's profile, on its own, for the popup that opens on any id in the dashboard.
    /// </summary>
    /// <remarks>
    /// The same object the investigation search carries under <c>playerProfile</c>, without the
    /// audit trail, ledger, chat and item history that go with it there. The popup reads only this,
    /// and was paying a dozen queries for the rest.
    /// </remarks>
    public Task<PlayerProfile?> PlayerProfileAsync(
        int playerId,
        NameValueCollection query,
        CancellationToken ct
    ) =>
        QueryAsync(
            db =>
                PlayerProfileAsync(
                    db,
                    playerId,
                    TimeWindow.ParseDateTime(query["since"]),
                    TimeWindow.ParseDateTime(query["until"]),
                    ct
                ),
            ct
        );

    /// <summary>
    /// Everything the player page and the entity popup show about one account.
    /// </summary>
    /// <remarks>
    /// Split out of the investigation search because the two callers want very different amounts of
    /// it: the popup that opens when an operator clicks an id anywhere in the dashboard shows a
    /// name, an avatar and three panels, while the investigation page also wants the audit trail,
    /// the ledger, the chat and the item history. Serving both from one answer made the popup pay
    /// for the investigation's queries.
    /// </remarks>
    private async Task<PlayerProfile?> PlayerProfileAsync(
        VortexDbContext db,
        int id,
        DateTime? since,
        DateTime? until,
        CancellationToken ct
    )
    {
        long playerIdLong = (long)id;
        DateTime profileWindowSince = since ?? DateTime.UtcNow.AddHours(-24);
        DateTime profileWindowUntil = until ?? DateTime.UtcNow;

        var player = await db
            .Players.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Motto,
                p.Figure,
                p.CreatedAt,
                p.UpdatedAt,
                // `players.status` is deliberately NOT projected: nothing in the
                // emulator ever writes it after account creation, so it answers
                // "Offline" for a connected player. `online` below is the real answer.
                gender = p.Gender.ToString(),
                perks = p.PlayerPerks.ToString(),
            })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        List<ProfileWallet> playerCurrencies = await db
            .PlayerCurrencies.AsNoTracking()
            .Where(pc => pc.PlayerEntityId == id)
            .Select(pc => new ProfileWallet(
                pc.CurrencyTypeEntityId,
                pc.Amount,
                pc.CurrencyTypeEntity != null
                    ? pc.CurrencyTypeEntity.Name ?? pc.CurrencyTypeEntity.CurrencyType.ToString()
                    : pc.CurrencyTypeEntityId.ToString(),
                pc.CurrencyTypeEntity != null ? pc.CurrencyTypeEntity.ActivityPointType : null
            ))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        List<ProfileRoomRow> ownedRooms = await db
            .Rooms.AsNoTracking()
            .Where(r => r.PlayerEntityId == id)
            .OrderByDescending(r => r.LastActive)
            .Take(8)
            .Select(r => new ProfileRoomRow(
                r.Id,
                r.Name,
                r.UsersNow,
                r.PlayersMax,
                r.LastActive,
                r.RoomModelEntity.Name
            ))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var ownedItems = await db
            .Furnitures.AsNoTracking()
            .Where(f => f.PlayerEntityId == id)
            .OrderByDescending(f => f.UpdatedAt)
            .Take(16)
            .Select(f => new
            {
                itemId = (long)f.Id,
                definitionId = (int?)f.FurnitureDefinitionEntityId,
                definitionName = f.FurnitureDefinitionEntity != null
                    ? f.FurnitureDefinitionEntity.Name
                    : null,
                f.RoomEntityId,
                roomName = f.RoomEntity != null ? f.RoomEntity.Name : null,
                roomX = (int?)f.X,
                roomY = (int?)f.Y,
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // BuildFurniIconUrl isn't SQL-translatable, so the furni icon is attached in a
        // second pass over the materialized rows (same shape as the catalog products).
        List<ProfileItemRow> ownedItemsWithIcons = ownedItems
            .Select(f => new ProfileItemRow(
                f.itemId,
                f.definitionId,
                f.definitionName,
                f.definitionName is null ? null : _assetUrls.FurniIcon(f.definitionName),
                f.RoomEntityId,
                f.roomName,
                f.roomX,
                f.roomY
            ))
            .ToList();

        List<ProfileEntryRow> roomEntries = await db
            .RoomEntryLogs.AsNoTracking()
            .Where(e => e.PlayerEntityId == id)
            .Where(e => e.CreatedAt >= profileWindowSince && e.CreatedAt <= profileWindowUntil)
            .OrderByDescending(e => e.CreatedAt)
            .Take(12)
            .Select(e => new ProfileEntryRow(
                e.CreatedAt,
                e.RoomEntityId,
                e.RoomEntity != null ? e.RoomEntity.Name : null
            ))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        List<ProfileChatRow> chatHistory = await db
            .Chatlogs.AsNoTracking()
            .Where(c => c.PlayerEntityId == id)
            .Where(c => c.CreatedAt >= profileWindowSince && c.CreatedAt <= profileWindowUntil)
            .OrderByDescending(c => c.CreatedAt)
            .Take(12)
            .Select(c => new ProfileChatRow(
                c.CreatedAt,
                c.RoomEntityId,
                c.RoomEntity != null ? c.RoomEntity.Name : null,
                c.Message
            ))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var itemEvents = await db
            .ItemEvents.AsNoTracking()
            .Where(i =>
                i.ActorPlayerId == playerIdLong
                || i.FromOwnerId == playerIdLong
                || i.ToOwnerId == playerIdLong
            )
            .Where(i => i.OccurredAt >= profileWindowSince && i.OccurredAt <= profileWindowUntil)
            .OrderByDescending(i => i.OccurredAt)
            .Take(24)
            .Select(i => new
            {
                i.OccurredAt,
                eventType = i.EventType.ToString(),
                itemId = i.ItemId,
                i.RoomId,
                actorPlayerId = i.ActorPlayerId,
                fromOwnerId = i.FromOwnerId,
                toOwnerId = i.ToOwnerId,
                correlationId = i.CorrelationId,
                i.Data,
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        List<int> itemRoomIds = DisplayNameQueries.NormalizeIds(itemEvents.Select(i => i.RoomId));

        Dictionary<int, string> itemRoomNames = await db.RoomNamesAsync(itemRoomIds, ct)
            .ConfigureAwait(false);

        List<int> itemPartyIds = DisplayNameQueries.NormalizeIds(
            itemEvents.SelectMany(i => new[] { i.actorPlayerId, i.fromOwnerId, i.toOwnerId })
        );

        Dictionary<int, string> itemPartyNames = await db.PlayerNamesAsync(itemPartyIds, ct)
            .ConfigureAwait(false);

        List<PlayerItemRow> itemEventsWithRooms = itemEvents
            .Select(i => new PlayerItemRow(
                i.OccurredAt,
                i.eventType,
                i.itemId,
                i.RoomId,
                i.RoomId != null && itemRoomNames.TryGetValue(i.RoomId.Value, out string? roomName)
                    ? roomName
                    : null,
                i.actorPlayerId,
                DisplayNameQueries.ResolvePlayerName(itemPartyNames, i.actorPlayerId),
                i.fromOwnerId,
                DisplayNameQueries.ResolvePlayerName(itemPartyNames, i.fromOwnerId),
                i.toOwnerId,
                DisplayNameQueries.ResolvePlayerName(itemPartyNames, i.toOwnerId),
                i.correlationId,
                i.Data
            ))
            .ToList();

        int auditCount = await db
            .AuditEvents.AsNoTracking()
            .Where(a => a.ActorPlayerId == id || a.TargetPlayerId == id)
            .Where(a => a.OccurredAt >= profileWindowSince && a.OccurredAt <= profileWindowUntil)
            .CountAsync(ct)
            .ConfigureAwait(false);

        int ledgerCount = await db
            .EconomyLedger.AsNoTracking()
            .Where(l => l.PlayerId == id)
            .Where(l => l.OccurredAt >= profileWindowSince && l.OccurredAt <= profileWindowUntil)
            .CountAsync(ct)
            .ConfigureAwait(false);

        int itemEventCount = await db
            .ItemEvents.AsNoTracking()
            .Where(i =>
                i.ActorPlayerId == playerIdLong
                || i.FromOwnerId == playerIdLong
                || i.ToOwnerId == playerIdLong
            )
            .Where(i => i.OccurredAt >= profileWindowSince && i.OccurredAt <= profileWindowUntil)
            .CountAsync(ct)
            .ConfigureAwait(false);

        int ownedRoomCount = await db
            .Rooms.AsNoTracking()
            .Where(r => r.PlayerEntityId == id)
            .CountAsync(ct)
            .ConfigureAwait(false);

        int ownedItemCount = await db
            .Furnitures.AsNoTracking()
            .Where(f => f.PlayerEntityId == id)
            .CountAsync(ct)
            .ConfigureAwait(false);

        return player is null
            ? null
            : new PlayerProfile(
                player.Id,
                player.Name,
                player.Motto,
                player.Figure,
                _assetUrls.AvatarImage(player.Figure),
                // The player page gates "kick" on this: there is nothing to disconnect
                // when the account is offline, and a button that always looks available
                // teaches the operator to ignore its result.
                _sessionGateway.IsOnline(player.Id),
                player.CreatedAt,
                player.UpdatedAt,
                player.gender,
                player.perks,
                new ProfileWindow(profileWindowSince, profileWindowUntil),
                new ProfileRooms(ownedRoomCount, ownedRooms),
                playerCurrencies,
                new ProfileInventory(ownedItemCount, ownedItemsWithIcons),
                new ProfileActivity(auditCount, ledgerCount, itemEventCount),
                new ProfileTimeline(roomEntries, chatHistory, itemEventsWithRooms)
            );
    }

    /// <summary>
    /// Everything known about one numeric id, read as a player id and as an item id at once.
    /// </summary>
    /// <remarks>
    /// It answers for an id that matches no player, because the same number is also an item id and
    /// a room id, and the history attached to it is the point. The sections are independent reads
    /// assembled into one answer rather than one query: an investigation opens this once and reads
    /// down it.
    /// </remarks>
    private async Task<IdSearch> SearchByIdAsync(
        VortexDbContext db,
        string term,
        int id,
        DateTime? since,
        DateTime? until,
        int limit,
        int page,
        int offset,
        CancellationToken ct
    )
    {
        PlayerProfile? playerProfile = await PlayerProfileAsync(db, id, since, until, ct)
            .ConfigureAwait(false);

        IQueryable<AuditEventEntity> audit = db
            .AuditEvents.AsNoTracking()
            .Where(a => a.ActorPlayerId == id || a.TargetPlayerId == id);

        if (since is not null)
        {
            audit = audit.Where(a => a.OccurredAt >= since.Value);
        }

        if (until is not null)
        {
            audit = audit.Where(a => a.OccurredAt <= until.Value);
        }

        IQueryable<EconomyLedgerEntity> ledger = db
            .EconomyLedger.AsNoTracking()
            .Where(l => l.PlayerId == id);

        if (since is not null)
        {
            ledger = ledger.Where(l => l.OccurredAt >= since.Value);
        }

        if (until is not null)
        {
            ledger = ledger.Where(l => l.OccurredAt <= until.Value);
        }

        IQueryable<ItemEventEntity> itemHistory = db
            .ItemEvents.AsNoTracking()
            .Where(i => i.ActorPlayerId == id || i.FromOwnerId == id || i.ToOwnerId == id);

        if (since is not null)
        {
            itemHistory = itemHistory.Where(i => i.OccurredAt >= since.Value);
        }

        if (until is not null)
        {
            itemHistory = itemHistory.Where(i => i.OccurredAt <= until.Value);
        }

        // Chat completes the timeline. An investigation that sees a furni leave the room
        // but not the sentence that preceded it stops one step short of the answer. It
        // stays under AuditRead: an in-context read on a player already being investigated,
        // which is exactly the line ChatlogsRead draws (it guards "who said this word,
        // anywhere" -- the one chat read that starts from no incident at all).
        IQueryable<RoomChatlogEntity> chat = db
            .Chatlogs.AsNoTracking()
            .Where(c => c.PlayerEntityId == id);

        if (since is not null)
        {
            chat = chat.Where(c => c.CreatedAt >= since.Value);
        }

        if (until is not null)
        {
            chat = chat.Where(c => c.CreatedAt <= until.Value);
        }

        var asActorRows = await audit
            .OrderByDescending(a => a.OccurredAt)
            .Skip(offset)
            .Take(limit)
            .Select(a => new
            {
                a.OccurredAt,
                category = a.Category.ToString(),
                a.Action,
                a.ActorPlayerId,
                a.TargetPlayerId,
                a.RoomId,
                a.Data,
                a.Result,
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        List<int> actorAndTargetIds = DisplayNameQueries.NormalizeIds(
            asActorRows.SelectMany(r => new[] { r.ActorPlayerId, r.TargetPlayerId })
        );

        Dictionary<int, string> actorAndTargetNames = await db.PlayerNamesAsync(
                actorAndTargetIds,
                ct
            )
            .ConfigureAwait(false);

        List<int> auditRoomIds = DisplayNameQueries.NormalizeIds(asActorRows.Select(r => r.RoomId));

        Dictionary<int, string> auditRoomNames = await db.RoomNamesAsync(auditRoomIds, ct)
            .ConfigureAwait(false);

        List<PlayerAuditRow> asActor = asActorRows
            .Select(r => new PlayerAuditRow(
                r.OccurredAt,
                r.category,
                r.Action,
                r.ActorPlayerId,
                DisplayNameQueries.ResolvePlayerName(actorAndTargetNames, r.ActorPlayerId),
                r.TargetPlayerId,
                DisplayNameQueries.ResolvePlayerName(actorAndTargetNames, r.TargetPlayerId),
                r.RoomId,
                r.RoomId != null && auditRoomNames.TryGetValue(r.RoomId.Value, out string? roomName)
                    ? roomName
                    : null,
                r.Result,
                r.Data
            ))
            .ToList();

        var itemHistoryRows = await itemHistory
            .OrderBy(i => i.OccurredAt)
            .Skip(offset)
            .Take(limit)
            .Select(i => new
            {
                i.OccurredAt,
                eventType = i.EventType.ToString(),
                i.ItemId,
                i.RoomId,
                i.ActorPlayerId,
                i.FromOwnerId,
                i.ToOwnerId,
                i.CorrelationId,
                i.Data,
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        List<int> itemHistoryRoomIds = DisplayNameQueries.NormalizeIds(
            itemHistoryRows.Select(row => row.RoomId)
        );

        List<int> itemHistoryPartyIds = DisplayNameQueries.NormalizeIds(
            itemHistoryRows.SelectMany(row =>
                new[] { row.ActorPlayerId, row.FromOwnerId, row.ToOwnerId }
            )
        );

        Dictionary<int, string> itemHistoryRoomNames = await db.RoomNamesAsync(
                itemHistoryRoomIds,
                ct
            )
            .ConfigureAwait(false);

        Dictionary<int, string> itemHistoryPartyNames = await db.PlayerNamesAsync(
                itemHistoryPartyIds,
                ct
            )
            .ConfigureAwait(false);

        List<PlayerItemRow> itemHistoryWithNames = itemHistoryRows
            .Select(row => new PlayerItemRow(
                row.OccurredAt,
                row.eventType,
                row.ItemId,
                row.RoomId,
                row.RoomId != null
                && itemHistoryRoomNames.TryGetValue(row.RoomId.Value, out string? roomName)
                    ? roomName
                    : null,
                row.ActorPlayerId,
                DisplayNameQueries.ResolvePlayerName(itemHistoryPartyNames, row.ActorPlayerId),
                row.FromOwnerId,
                DisplayNameQueries.ResolvePlayerName(itemHistoryPartyNames, row.FromOwnerId),
                row.ToOwnerId,
                DisplayNameQueries.ResolvePlayerName(itemHistoryPartyNames, row.ToOwnerId),
                row.CorrelationId,
                row.Data
            ))
            .ToList();

        List<PlayerLedgerRow> ledgerRows = await ledger
            .OrderByDescending(l => l.OccurredAt)
            .Skip(offset)
            .Take(limit)
            .Select(l => new PlayerLedgerRow(
                l.OccurredAt,
                l.Currency,
                l.Delta,
                l.BalanceAfter,
                l.ActivityPointType,
                l.CorrelationId
            ))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        List<PlayerChatRow> chatRows = await chat.OrderByDescending(c => c.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .Select(c => new PlayerChatRow(
                c.CreatedAt,
                c.RoomEntityId,
                c.RoomEntity != null ? c.RoomEntity.Name : null,
                c.Message,
                c.TargetPlayerEntityId,
                c.TargetPlayerEntity != null ? c.TargetPlayerEntity.Name : null
            ))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return new IdSearch(
            term,
            page,
            limit,
            offset,
            asActor,
            playerProfile,
            ledgerRows,
            itemHistoryWithNames,
            chatRows
        );
    }

    public Task<RoomTimeline?> RoomTimelineAsync(
        int roomId,
        NameValueCollection query,
        CancellationToken ct
    ) =>
        QueryAsync<RoomTimeline?>(
            async db =>
            {
                var room = await db
                    .Rooms.AsNoTracking()
                    .Where(r => r.Id == roomId)
                    .Select(r => new
                    {
                        r.Id,
                        r.Name,
                        r.Description,
                        OwnerPlayerId = r.PlayerEntityId,
                        RoomOwnerId = r.PlayerEntityId,
                        RoomOwnerName = r.PlayerEntity != null ? r.PlayerEntity.Name : null,
                        r.UsersNow,
                        r.PlayersMax,
                        LastActive = r.LastActive,
                        ModelName = r.RoomModelEntity.Name,
                        r.IsStaffPick,
                    })
                    .FirstOrDefaultAsync(ct)
                    .ConfigureAwait(false);

                if (room is null)
                {
                    return null;
                }

                int limit = QueryValues.Limit(query["limit"], 80, 500);
                int page = QueryValues.Page(query["page"]);
                int offset = Math.Max(0, (page - 1) * limit);
                int take = offset + limit;
                DateTime? since = TimeWindow.ParseDateTime(query["since"]);
                DateTime? until = TimeWindow.ParseDateTime(query["until"]);

                IQueryable<RoomEntryLogEntity> entriesQuery = db
                    .RoomEntryLogs.AsNoTracking()
                    .Where(e => e.RoomEntityId == roomId);
                IQueryable<RoomChatlogEntity> chatQuery = db
                    .Chatlogs.AsNoTracking()
                    .Where(c => c.RoomEntityId == roomId);
                IQueryable<ItemEventEntity> itemQuery = db
                    .ItemEvents.AsNoTracking()
                    .Where(i => i.RoomId == roomId);

                if (since is not null)
                {
                    entriesQuery = entriesQuery.Where(e => e.CreatedAt >= since.Value);
                    chatQuery = chatQuery.Where(c => c.CreatedAt >= since.Value);
                    itemQuery = itemQuery.Where(i => i.OccurredAt >= since.Value);
                }

                if (until is not null)
                {
                    entriesQuery = entriesQuery.Where(e => e.CreatedAt <= until.Value);
                    chatQuery = chatQuery.Where(c => c.CreatedAt <= until.Value);
                    itemQuery = itemQuery.Where(i => i.OccurredAt <= until.Value);
                }

                int entryCount = await entriesQuery.CountAsync(ct).ConfigureAwait(false);
                int chatCount = await chatQuery.CountAsync(ct).ConfigureAwait(false);
                int itemCount = await itemQuery.CountAsync(ct).ConfigureAwait(false);

                List<RoomTimelineRow> entryTimeline = await entriesQuery
                    .OrderByDescending(e => e.CreatedAt)
                    .Take(take)
                    .Select(e => new RoomTimelineRow(
                        e.CreatedAt,
                        "entry",
                        e.PlayerEntityId,
                        e.PlayerEntity != null ? e.PlayerEntity.Name : null,
                        null,
                        null,
                        null,
                        null
                    ))
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                List<RoomTimelineRow> chatTimeline = await chatQuery
                    .OrderByDescending(c => c.CreatedAt)
                    .Take(take)
                    .Select(c => new RoomTimelineRow(
                        c.CreatedAt,
                        "chat",
                        c.PlayerEntityId,
                        c.PlayerEntity != null ? c.PlayerEntity.Name : null,
                        c.Message,
                        c.TargetPlayerEntityId,
                        c.TargetPlayerEntity != null ? c.TargetPlayerEntity.Name : null,
                        null
                    ))
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                var rawItemTimeline = await itemQuery
                    .OrderByDescending(i => i.OccurredAt)
                    .Take(take)
                    .Select(i => new
                    {
                        CreatedAt = i.OccurredAt,
                        EventType = "item",
                        i.ActorPlayerId,
                        Message = (string?)i.Data,
                        i.ToOwnerId,
                        ItemId = (long?)i.ItemId,
                    })
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                List<int> itemPlayerIds = DisplayNameQueries.NormalizeIds(
                    rawItemTimeline.SelectMany(i => new[] { i.ActorPlayerId, i.ToOwnerId })
                );

                Dictionary<int, string> itemPlayerNames = await db.PlayerNamesAsync(
                        itemPlayerIds,
                        ct
                    )
                    .ConfigureAwait(false);

                List<RoomTimelineRow> itemTimeline = rawItemTimeline
                    .Select(i => new RoomTimelineRow(
                        i.CreatedAt,
                        i.EventType,
                        DisplayNameQueries.ToPlayerId(i.ActorPlayerId),
                        DisplayNameQueries.ResolvePlayerName(itemPlayerNames, i.ActorPlayerId),
                        i.Message,
                        DisplayNameQueries.ToPlayerId(i.ToOwnerId),
                        DisplayNameQueries.ResolvePlayerName(itemPlayerNames, i.ToOwnerId),
                        i.ItemId
                    ))
                    .ToList();

                List<RoomTimelineRow> timeline = entryTimeline
                    .Concat(chatTimeline)
                    .Concat(itemTimeline)
                    .OrderByDescending(e => e.CreatedAt)
                    .ThenBy(e => e.EventType)
                    .Skip(offset)
                    .Take(limit)
                    .ToList();

                return new RoomTimeline(
                    new RoomTimelineHeader(
                        room.Id,
                        room.Name,
                        room.Description,
                        room.RoomOwnerId,
                        room.RoomOwnerName,
                        room.OwnerPlayerId,
                        room.UsersNow,
                        room.PlayersMax,
                        room.LastActive,
                        room.ModelName,
                        room.IsStaffPick
                    ),
                    page,
                    limit,
                    offset,
                    timeline.Count,
                    entryCount + chatCount + itemCount,
                    new RoomTimelineTotals(entryCount, chatCount, itemCount),
                    timeline
                );
            },
            ct
        );

    public Task<PlayerDirectoryPage> PlayersAsync(NameValueCollection query, CancellationToken ct)
    {
        HashSet<int> online = _sessionGateway.GetOnlinePlayerIds().Select(p => p.Value).ToHashSet();

        return QueryAsync<PlayerDirectoryPage>(
            async db =>
            {
                string term = (query["q"] ?? string.Empty).Trim();
                int limit = QueryValues.Limit(query["limit"], 50, 200);

                // Without an offset the picker could only ever reach the first page: "Load more" is
                // driven by hasMore, and a directory that never reports one silently pretends the
                // hotel has 200 accounts.
                int offset = int.TryParse(query["offset"], out int parsedOffset)
                    ? Math.Max(0, parsedOffset)
                    : 0;

                List<PlayerRow> rows;
                int total;

                if (term.Length == 0)
                {
                    // Browsing: surface online players first, then the most recent accounts. Paging
                    // walks that same order -- the online block, then the rest -- so scrolling past
                    // the last online player continues into the offline ones instead of repeating
                    // them.
                    List<int> onlineIds = online.ToList();

                    total = await db.Players.AsNoTracking().CountAsync(ct).ConfigureAwait(false);

                    List<PlayerRow> onlineRows =
                        onlineIds.Count > 0 && offset < onlineIds.Count
                            ? await db
                                .Players.AsNoTracking()
                                .Where(p => onlineIds.Contains(p.Id))
                                .OrderBy(p => p.Name)
                                .Skip(offset)
                                .Take(limit)
                                .Select(p => new PlayerRow(p.Id, p.Name, p.Figure))
                                .ToListAsync(ct)
                                .ConfigureAwait(false)
                            : new List<PlayerRow>();

                    int remaining = limit - onlineRows.Count;

                    // Past the online block the offset counts from the start of the offline ones.
                    int fillOffset = Math.Max(0, offset - onlineIds.Count);

                    List<PlayerRow> fillRows =
                        remaining > 0
                            ? await db
                                .Players.AsNoTracking()
                                .Where(p => !onlineIds.Contains(p.Id))
                                .OrderByDescending(p => p.Id)
                                .Skip(fillOffset)
                                .Take(remaining)
                                .Select(p => new PlayerRow(p.Id, p.Name, p.Figure))
                                .ToListAsync(ct)
                                .ConfigureAwait(false)
                            : new List<PlayerRow>();

                    rows = onlineRows.Concat(fillRows).ToList();
                }
                else
                {
                    IQueryable<PlayerEntity> players = db.Players.AsNoTracking();

                    if (int.TryParse(term, out int id))
                    {
                        players = players.Where(p => p.Name.Contains(term) || p.Id == id);
                    }
                    else
                    {
                        players = players.Where(p => p.Name.Contains(term));
                    }

                    total = await players.CountAsync(ct).ConfigureAwait(false);

                    rows = await OrderPlayers(players, query["sort"])
                        .Skip(offset)
                        .Take(limit)
                        .Select(p => new PlayerRow(p.Id, p.Name, p.Figure))
                        .ToListAsync(ct)
                        .ConfigureAwait(false);
                }

                // Applied after the projection because "online" is session state, not a column.
                if (string.Equals(query["online"], "true", StringComparison.OrdinalIgnoreCase))
                {
                    rows = rows.Where(p => online.Contains(p.Id)).ToList();
                }

                List<PlayerDirectoryRow> items = rows.Select(p => new PlayerDirectoryRow(
                        p.Id,
                        p.Name,
                        _assetUrls.AvatarImage(p.Figure),
                        online.Contains(p.Id)
                    ))
                    .ToList();

                // Online-first is the browsing default; an explicit sort is the operator overriding
                // it, and re-sorting here would silently undo what they asked for.
                items = (query["sort"] ?? string.Empty) switch
                {
                    "id" => items.OrderBy(p => p.Id).ToList(),
                    "idDesc" => items.OrderByDescending(p => p.Id).ToList(),
                    "name" => items.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList(),
                    _ => items
                        .OrderByDescending(p => p.Online)
                        .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                };

                return new PlayerDirectoryPage(
                    items.Count,
                    total,
                    offset,
                    // The online filter drops rows after the projection, so the last page can come
                    // back short; comparing against the unfiltered total would offer a "Load more"
                    // that returns nothing. A short page is the end of the list.
                    items.Count == limit
                        && offset + items.Count < total,
                    online.Count,
                    items
                );
            },
            ct
        );
    }

    /// <summary>
    /// Room search for the shared picker: <c>?q=</c> matches a room name or an exact id, and an empty
    /// term browses the most recently active rooms. Exists so a surface that pins something to a room
    /// (a survey, for one) can hand back an id the operator never had to look up by hand.
    /// </summary>
    public Task<RoomDirectoryPage> RoomsDirectoryAsync(
        NameValueCollection query,
        CancellationToken ct
    ) =>
        QueryAsync<RoomDirectoryPage>(
            async db =>
            {
                string term = (query["q"] ?? string.Empty).Trim();
                int limit = QueryValues.Limit(query["limit"], 50, 200);
                int offset = int.TryParse(query["offset"], out int parsedOffset)
                    ? Math.Max(0, parsedOffset)
                    : 0;

                IQueryable<RoomEntity> rooms = db.Rooms.AsNoTracking();

                if (term.Length > 0)
                {
                    rooms = int.TryParse(term, out int id)
                        ? rooms.Where(r => r.Name.Contains(term) || r.Id == id)
                        : rooms.Where(r => r.Name.Contains(term));
                }

                int total = await rooms.CountAsync(ct).ConfigureAwait(false);

                List<RoomDirectoryRow> items = await OrderRooms(rooms, query["sort"])
                    .Skip(offset)
                    .Take(limit)
                    .Select(r => new RoomDirectoryRow(
                        r.Id,
                        r.Name,
                        r.PlayerEntity != null ? r.PlayerEntity.Name : null,
                        r.UsersNow,
                        r.PlayersMax,
                        r.LastActive
                    ))
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                return new RoomDirectoryPage(
                    items.Count,
                    total,
                    offset,
                    offset + items.Count < total,
                    items
                );
            },
            ct
        );

    /// <summary>
    /// Batch avatar-head lookup: <c>?ids=1,2,3</c> → <c>{ items: [{ id, avatarUrl }] }</c>. Lets any
    /// dashboard surface that already shows a player id/name (audit, moderation, CFH, economy…) render
    /// the real Habbo avatar head without every one of those endpoints having to load <c>Figure</c>
    /// and emit its own <c>avatarUrl</c>. The frontend batches the ids it needs per tick through
    /// <c>lib/avatars.js</c> and caches the result. A player with no figure yields a null url (the UI
    /// falls back to a neutral head), and the id cap keeps a single request bounded.
    /// </summary>
    public Task<AvatarBatch> AvatarsAsync(NameValueCollection query, CancellationToken ct)
    {
        List<int> ids = (query["ids"] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => int.TryParse(part, out int parsed) ? parsed : (int?)null)
            .Where(parsed => parsed.HasValue)
            .Select(parsed => parsed!.Value)
            .Distinct()
            .Take(200)
            .ToList();

        if (ids.Count == 0)
        {
            return Task.FromResult(new AvatarBatch([]));
        }

        return QueryAsync<AvatarBatch>(
            async db =>
            {
                var rows = await db
                    .Players.AsNoTracking()
                    .Where(p => ids.Contains(p.Id))
                    .Select(p => new { p.Id, p.Figure })
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                List<AvatarBatchRow> items = rows.Select(p => new AvatarBatchRow(
                        p.Id,
                        _assetUrls.AvatarImage(p.Figure)
                    ))
                    .ToList();

                return new AvatarBatch(items);
            },
            ct
        );
    }

    public Task<FurnitureDirectoryPage> FurnitureDefinitionsAsync(
        NameValueCollection query,
        CancellationToken ct
    ) =>
        QueryAsync<FurnitureDirectoryPage>(
            async db =>
            {
                string term = (query["q"] ?? string.Empty).Trim();
                int limit = QueryValues.Limit(query["limit"], 50, 200);

                // The picker used to stop at the first page with no way to ask for more, so a hotel
                // with thousands of definitions could only ever reach the first fifty by name.
                int offset = int.TryParse(query["offset"], out int parsedOffset)
                    ? Math.Max(0, parsedOffset)
                    : 0;

                IQueryable<FurnitureDefinitionEntity> definitions =
                    db.FurnitureDefinitions.AsNoTracking();

                if (term.Length > 0)
                {
                    if (int.TryParse(term, out int id))
                    {
                        definitions = definitions.Where(f =>
                            f.Name.Contains(term) || f.Id == id || f.SpriteId == id
                        );
                    }
                    else
                    {
                        definitions = definitions.Where(f => f.Name.Contains(term));
                    }
                }

                // The one filter that answers "I forgot the name but I know what it does" -- every
                // dice, every roller, every wired action, without knowing a single classname.
                string logic = (query["logic"] ?? string.Empty).Trim();
                if (logic.Length > 0)
                {
                    definitions = definitions.Where(f => f.Logic == logic);
                }

                int total = await definitions.CountAsync(ct).ConfigureAwait(false);

                var rows = await OrderDefinitions(definitions, query["sort"])
                    .Skip(offset)
                    .Take(limit)
                    .Select(f => new
                    {
                        f.Id,
                        f.SpriteId,
                        f.Name,
                        f.Logic,
                        type = f.ProductType.ToString(),
                        category = f.FurniCategory.ToString(),
                        f.Width,
                        f.Length,
                        f.CanTrade,
                        f.CanSell,
                    })
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                List<FurnitureDirectoryRow> items = rows.Select(f => new FurnitureDirectoryRow(
                        f.Id,
                        f.SpriteId,
                        f.Name,
                        f.Logic,
                        f.type,
                        f.category,
                        f.Width,
                        f.Length,
                        f.CanTrade,
                        f.CanSell,
                        _assetUrls.FurniIcon(f.Name)
                    ))
                    .ToList();

                // hasMore rather than a page count: the picker appends, it never jumps.
                return new FurnitureDirectoryPage(
                    items.Count,
                    total,
                    offset,
                    offset + items.Count < total,
                    items
                );
            },
            ct
        );

    // Picker sorting. A picker that can only order by name is unusable the moment the operator
    // remembers the id, the sprite or what the thing does but not what it is called. Unknown values
    // fall back to the browsing default rather than erroring, because the sort arrives from a query
    // string and a typo there should not be a failed search.
    private static IQueryable<PlayerEntity> OrderPlayers(
        IQueryable<PlayerEntity> players,
        string? sort
    ) =>
        sort switch
        {
            "id" => players.OrderBy(p => p.Id),
            "idDesc" => players.OrderByDescending(p => p.Id),
            _ => players.OrderBy(p => p.Name),
        };

    private static IQueryable<RoomEntity> OrderRooms(IQueryable<RoomEntity> rooms, string? sort) =>
        sort switch
        {
            "name" => rooms.OrderBy(r => r.Name),
            "id" => rooms.OrderBy(r => r.Id),
            "idDesc" => rooms.OrderByDescending(r => r.Id),
            // Browsing shows the liveliest rooms first; a search still ranks by activity so the busy
            // "Lobby" beats an abandoned one of the same name.
            _ => rooms.OrderByDescending(r => r.UsersNow).ThenByDescending(r => r.LastActive),
        };

    private static IQueryable<FurnitureDefinitionEntity> OrderDefinitions(
        IQueryable<FurnitureDefinitionEntity> definitions,
        string? sort
    ) =>
        sort switch
        {
            "id" => definitions.OrderBy(f => f.Id),
            "idDesc" => definitions.OrderByDescending(f => f.Id),
            "sprite" => definitions.OrderBy(f => f.SpriteId),
            "logic" => definitions.OrderBy(f => f.Logic).ThenBy(f => f.Name),
            _ => definitions.OrderBy(f => f.Name),
        };

    /// <summary>
    /// The picture of a catalog product, whatever kind of thing it is.
    /// </summary>
    /// <remarks>
    /// A catalog product is not always furniture, and only furniture has a furni icon. Badges are the
    /// case that matters here: on this hotel they are ~25 000 of the ~105 000 products, half of them
    /// with no furniture definition at all, so building the icon from the definition name left every
    /// one of them blank — an operator picking a badge to sell had nothing to look at. Their code
    /// lives in <c>ExtraParam</c>, and 98% of the codes in this database have an asset on disk.
    /// <para>
    /// Effects are resolved the same way for the same reason. Pets are left alone deliberately: their
    /// <c>ExtraParam</c> is a pet type id, and there is no per-type image in the asset pack to point
    /// at — a URL that 404s is worse than an honest fallback icon.
    /// </para>
    /// </remarks>
    private sealed record PlayerRow(int Id, string Name, string Figure);
}
