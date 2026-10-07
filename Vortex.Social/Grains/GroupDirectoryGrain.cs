using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Orleans;
using Vortex.Database.Context;
using Vortex.Database.Entities.Groups;
using Vortex.Database.Entities.Players;
using Vortex.Database.Entities.Room;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Groups;
using Vortex.Primitives.Groups.Enums;
using Vortex.Primitives.Groups.Grains;
using Vortex.Primitives.Groups.Providers;
using Vortex.Primitives.Groups.Snapshots;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Enums.Wallet;
using Vortex.Primitives.Players.Wallet;
using Vortex.Primitives.Server.Grains;

namespace Vortex.Social.Grains;

internal sealed class GroupDirectoryGrain(
    IDbContextFactory<VortexDbContext> dbCtxFactory,
    IGrainFactory grainFactory,
    IGroupBadgePartProvider badgePartProvider,
    IEventPublisher events,
    ICancellableEventPublisher cancellableEvents,
    ILogger<GroupDirectoryGrain> logger
) : Grain, IGroupDirectoryGrain
{
    private const string CatalogPurchaseTypeGuild = "Guild";

    // Forum list tabs, per the client's groupforum.view.forums_list.{code} localization keys.
    private const int ForumsListMostViewed = 1;
    private const int ForumsListMyForums = 2;

    public async Task<GroupCreationInfoSnapshot> GetCreationInfoAsync(
        PlayerId player,
        CancellationToken ct
    )
    {
        await using VortexDbContext dbCtx = await dbCtxFactory.CreateDbContextAsync(ct);

        int creationCost = await grainFactory
            .GetServerConfigGrain()
            .GetIntAsync(
                GroupConfig.CreationCostInCreditsKey,
                GroupConfig.CreationCostInCreditsDefault
            )
            .ConfigureAwait(true);

        int playerId = player.Value;

        List<GroupRoomSnapshot> rooms = await dbCtx
            .Rooms.AsNoTracking()
            .Where(r => r.PlayerEntityId == playerId && r.DeletedAt == null)
            .OrderBy(r => r.Id)
            .Select(r => new GroupRoomSnapshot
            {
                RoomId = r.Id,
                RoomName = r.Name,
                // A room can host a guild only if it is not already a guild base.
                HasControllers = r.GroupEntityId == null,
            })
            .ToListAsync(ct);

        return new GroupCreationInfoSnapshot
        {
            CostInCredits = creationCost,
            Rooms = rooms,
            // Default starting badge the wizard pre-fills; the player edits it before confirming.
            BadgeParts = GuildBadgeLibrary.DefaultBadgeParts,
        };
    }

    public async Task<int?> CreateGroupAsync(
        PlayerId owner,
        string name,
        string description,
        int primaryColorId,
        int secondaryColorId,
        int baseRoomId,
        IReadOnlyList<int> badgeParts,
        CancellationToken ct
    )
    {
        int ownerId = owner.Value;

        int creationCost = await grainFactory
            .GetServerConfigGrain()
            .GetIntAsync(
                GroupConfig.CreationCostInCreditsKey,
                GroupConfig.CreationCostInCreditsDefault
            )
            .ConfigureAwait(true);

        // Preflight check on its own short-lived context, purely to fail fast (and avoid debiting
        // the wallet) before ever publishing the cancellable event below. The authoritative check
        // happens again inside the retryable transaction further down.
        await using (VortexDbContext preflightCtx = await dbCtxFactory.CreateDbContextAsync(ct))
        {
            RoomEntity? preflightRoom = await preflightCtx
                .Rooms.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == baseRoomId && r.DeletedAt == null, ct);

            // Must own the room, and the room must not already be a guild base.
            if (
                preflightRoom is null
                || preflightRoom.PlayerEntityId != ownerId
                || preflightRoom.GroupEntityId != null
            )
            {
                return null;
            }

            bool ownerExists = await preflightCtx.Players.AnyAsync(p => p.Id == ownerId, ct);
            if (!ownerExists)
            {
                return null;
            }
        }

        EventContext creatingContext = await cancellableEvents
            .PublishCancellableAsync(
                new GroupCreatingEvent(ownerId, name, baseRoomId, creationCost),
                ct
            )
            .ConfigureAwait(true);

        if (creatingContext.Cancel)
        {
            logger.LogInformation(
                "Group creation for player {OwnerId} on room {RoomId} was cancelled: {Reason}",
                ownerId,
                baseRoomId,
                creatingContext.CancelReason ?? string.Empty
            );

            return null;
        }

        // A fresh DbContext is created per attempt (rather than shared across retries) so that a
        // transient failure retried by the MySQL execution strategy never resubmits entities left
        // half-tracked by a rolled-back attempt.
        await using VortexDbContext strategyProbe = await dbCtxFactory.CreateDbContextAsync(ct);
        IExecutionStrategy strategy = strategyProbe.Database.CreateExecutionStrategy();

        // Charge the creation cost through the shared debit-then-grant executor, exactly like every
        // other catalog purchase: the wallet writes the economy ledger entry, emits
        // CurrencyChangedEvent and pushes the new balance, and if the insert below throws (most
        // likely the unique guild-per-room constraint) the credits are refunded instead of lost.
        WalletPurchaseResult<int> purchase = await grainFactory
            .GetPlayerWalletGrain(ownerId)
            .ExecutePurchaseAsync(
                [
                    new WalletDebitRequest
                    {
                        CurrencyKind = new CurrencyKind { CurrencyType = CurrencyType.Credits },
                        Amount = creationCost,
                    },
                ],
                token =>
                    strategy.ExecuteAsync(async () =>
                    {
                        await using VortexDbContext dbCtx = await dbCtxFactory.CreateDbContextAsync(
                            token
                        );

                        RoomEntity? room = await dbCtx.Rooms.FirstOrDefaultAsync(
                            r => r.Id == baseRoomId && r.DeletedAt == null,
                            token
                        );

                        if (
                            room is null
                            || room.PlayerEntityId != ownerId
                            || room.GroupEntityId != null
                        )
                        {
                            throw new InvalidOperationException(
                                $"Room {baseRoomId} is no longer a valid guild base for player {ownerId}."
                            );
                        }

                        PlayerEntity? ownerEntity = await dbCtx.Players.FindAsync([ownerId], token);

                        if (ownerEntity is null)
                        {
                            throw new InvalidOperationException(
                                $"Player {ownerId} no longer exists."
                            );
                        }

                        // The group row and the room back-link must land together: a committed group
                        // whose room never got linked would permanently burn that room as a guild base.
                        await using IDbContextTransaction tx = await dbCtx
                            .Database.BeginTransactionAsync(token)
                            .ConfigureAwait(true);

                        GroupEntity created = GroupFactory.Create(
                            name,
                            BuildBadgeCode(badgeParts),
                            baseRoomId,
                            ownerId,
                            GroupType.Open,
                            primaryColorId.ToString(),
                            secondaryColorId.ToString(),
                            string.IsNullOrEmpty(description) ? null : description
                        );
                        created.RoomEntity = room;
                        created.OwnerPlayerEntity = ownerEntity;

                        dbCtx.Groups.Add(created);
                        await dbCtx.SaveChangesAsync(token).ConfigureAwait(true);

                        // Link the room back to the group and enrol the owner as an admin member.
                        room.GroupEntityId = created.Id;
                        dbCtx.GroupMembers.Add(
                            new GroupMemberEntity
                            {
                                GroupEntityId = created.Id,
                                PlayerEntityId = ownerId,
                                Rank = GroupMemberRank.Admin,
                                GroupEntity = created,
                                PlayerEntity = ownerEntity,
                            }
                        );

                        await dbCtx.SaveChangesAsync(token).ConfigureAwait(true);
                        await tx.CommitAsync(token).ConfigureAwait(true);

                        return created.Id;
                    }),
                logger,
                ct
            )
            .ConfigureAwait(true);

        if (!purchase.Succeeded)
        {
            logger.LogInformation(
                "Player {OwnerId} could not afford guild creation ({Cost} credits)",
                ownerId,
                creationCost
            );
            return null;
        }

        int groupId = purchase.Reward;

        // Hook the spend into the catalog purchase path, exactly like CatalogPurchaseGrain: track
        // the credit spend (Membership payday) and raise CatalogPurchasedEvent so guild creation shows up
        // in catalog purchase metrics/audit alongside every other purchase.
        await grainFactory
            .GetPlayerGrain(owner)
            .TrackCreditSpendAsync(creationCost, ct)
            .ConfigureAwait(true);

        await events
            .PublishAsync(
                new CatalogPurchasedEvent(ownerId, CatalogPurchaseTypeGuild, 0, 1, creationCost),
                ct
            )
            .ConfigureAwait(true);

        await events
            .PublishAsync(
                new GroupCreatedEvent(ownerId, groupId, name, baseRoomId, creationCost),
                ct
            )
            .ConfigureAwait(true);

        logger.LogInformation(
            "Player {OwnerId} created group {GroupId} on room {RoomId}",
            ownerId,
            groupId,
            baseRoomId
        );

        return groupId;
    }

    public async Task<List<GuildInfoSnapshot>> GetMembershipsAsync(
        PlayerId player,
        CancellationToken ct
    )
    {
        await using VortexDbContext dbCtx = await dbCtxFactory.CreateDbContextAsync(ct);

        int playerId = player.Value;

        int? favouriteGroupId = await dbCtx
            .Players.AsNoTracking()
            .Where(p => p.Id == playerId)
            .Select(p => p.FavouriteGroupId)
            .FirstOrDefaultAsync(ct);

        // Groups the player belongs to (membership table already includes the owner).
        List<int> groupIds = await dbCtx
            .GroupMembers.AsNoTracking()
            .Where(m => m.PlayerEntityId == playerId && m.DeletedAt == null)
            .Select(m => m.GroupEntityId)
            .ToListAsync(ct);

        if (groupIds.Count == 0)
        {
            return [];
        }

        List<GroupEntity> groups = await dbCtx
            .Groups.AsNoTracking()
            .Include(g => g.ForumSettings)
            .Where(g => groupIds.Contains(g.Id) && g.DeletedAt == null)
            .ToListAsync(ct);

        return groups
            .Select(g => new GuildInfoSnapshot
            {
                GroupId = g.Id,
                GroupName = g.Name,
                BadgeCode = g.Badge,
                // ColorOne/ColorTwo store the palette colour id; resolve to hex for the client.
                PrimaryColor = badgePartProvider.ResolveColorHex(g.ColorOne),
                SecondaryColor = badgePartProvider.ResolveColorHex(g.ColorTwo),
                Favourite = favouriteGroupId == g.Id,
                OwnerId = g.OwnerPlayerEntityId,
                HasForum = g.ForumSettings?.Enabled ?? false,
            })
            .ToList();
    }

    public async Task SetFavouriteAsync(
        PlayerId player,
        int groupId,
        bool favourite,
        CancellationToken ct
    )
    {
        int playerId = player.Value;

        await using VortexDbContext dbCtx = await dbCtxFactory.CreateDbContextAsync(ct);

        // Only a member may favourite a group; clearing is unconditional.
        if (favourite)
        {
            bool isMember = await dbCtx.GroupMembers.AnyAsync(
                m =>
                    m.GroupEntityId == groupId
                    && m.PlayerEntityId == playerId
                    && m.DeletedAt == null,
                ct
            );
            if (!isMember)
            {
                return;
            }
        }

        int newValue = favourite ? groupId : 0;

        // PlayerGrain owns players.favourite_group_id: it caches the value for the avatar snapshot
        // and re-badges the avatar in the room, so writing the column from here would leave that
        // cache — and the badge everyone in the room can see — stale.
        await grainFactory
            .GetPlayerGrain(player)
            .SetFavouriteGroupAsync(newValue, ct)
            .ConfigureAwait(true);

        await events
            .PublishAsync(new GroupFavouriteChangedEvent(playerId, favourite ? groupId : null), ct)
            .ConfigureAwait(true);
    }

    public Task<GroupEditorDataSnapshot> GetEditorDataAsync(CancellationToken ct)
    {
        // Badge designer palette: base shapes, overlay symbols and colour swatches the client
        // renders in the editor. Sourced from GuildBadgeLibrary (the part/colour ids are stable
        // and are what the persisted badge code references).
        return Task.FromResult(
            new GroupEditorDataSnapshot
            {
                BaseParts = badgePartProvider.BaseParts.ToList(),
                LayerParts = badgePartProvider.LayerParts.ToList(),
                BadgeColors = badgePartProvider.Colors.ToList(),
                PrimaryColors = badgePartProvider.Colors.ToList(),
                SecondaryColors = badgePartProvider.Colors.ToList(),
            }
        );
    }

    public async Task<List<GroupBadgeSnapshot>> GetBadgesAsync(
        PlayerId player,
        CancellationToken ct
    )
    {
        await using VortexDbContext dbCtx = await dbCtxFactory.CreateDbContextAsync(ct);

        int playerId = player.Value;

        List<int> groupIds = await dbCtx
            .GroupMembers.AsNoTracking()
            .Where(m => m.PlayerEntityId == playerId && m.DeletedAt == null)
            .Select(m => m.GroupEntityId)
            .ToListAsync(ct);

        if (groupIds.Count == 0)
        {
            return [];
        }

        return await dbCtx
            .Groups.AsNoTracking()
            .Where(g => groupIds.Contains(g.Id) && g.DeletedAt == null)
            .Select(g => new GroupBadgeSnapshot { GroupId = g.Id, BadgeCode = g.Badge })
            .ToListAsync(ct);
    }

    public async Task<ForumsListPageSnapshot> GetForumsListAsync(
        PlayerId player,
        int listCode,
        int startIndex,
        int amount,
        CancellationToken ct
    )
    {
        await using VortexDbContext dbCtx = await dbCtxFactory.CreateDbContextAsync(ct);

        IServerConfigGrain config = grainFactory.GetServerConfigGrain();
        int maxForumPageSize = await config
            .GetIntAsync(GroupConfig.MaxForumPageSizeKey, GroupConfig.MaxForumPageSizeDefault)
            .ConfigureAwait(true);
        int defaultForumPageSize = await config
            .GetIntAsync(
                GroupConfig.DefaultForumPageSizeKey,
                GroupConfig.DefaultForumPageSizeDefault
            )
            .ConfigureAwait(true);

        int take = (amount <= 0 || amount > maxForumPageSize) ? defaultForumPageSize : amount;
        int skip = Math.Max(startIndex, 0);

        IQueryable<GroupEntity> enabledForums = dbCtx
            .Groups.AsNoTracking()
            .Where(g => g.DeletedAt == null && g.ForumSettings!.Enabled);

        // listCode is the tab the client is showing. Verified against the localization keys
        // groupforum.view.forums_list.{0,1,2}: 0 = Most Active, 1 = Most Viewed, 2 = My Forums.
        if (listCode == ForumsListMyForums)
        {
            int viewerId = player.Value;

            enabledForums = enabledForums.Where(g =>
                dbCtx.GroupMembers.Any(m =>
                    m.GroupEntityId == g.Id && m.PlayerEntityId == viewerId && m.DeletedAt == null
                )
            );
        }

        int totalAmount = await enabledForums.CountAsync(ct);

        IOrderedQueryable<GroupEntity> orderedForums = listCode switch
        {
            ForumsListMostViewed => enabledForums.OrderByDescending(g =>
                g.ForumSettings!.ViewCount
            ),
            // "Most active" and "My forums" both rank by recent posting activity.
            _ => enabledForums.OrderByDescending(g =>
                dbCtx
                    .GroupForumThreads.Where(t => t.GroupEntityId == g.Id && t.DeletedAt == null)
                    .Max(t => (DateTime?)(t.LastPostAt ?? t.CreatedAt))
            ),
        };

        var groups = await orderedForums
            .ThenByDescending(g => g.Id)
            .Skip(skip)
            .Take(take)
            .Select(g => new
            {
                g.Id,
                g.Name,
                g.Description,
                g.Badge,
            })
            .ToListAsync(ct);

        if (groups.Count == 0)
        {
            return new ForumsListPageSnapshot
            {
                ListCode = listCode,
                TotalAmount = totalAmount,
                StartIndex = startIndex,
                Forums = [],
            };
        }

        List<int> groupIds = groups.Select(g => g.Id).ToList();

        Dictionary<int, int> readCounts = await dbCtx
            .GroupForumReadMarkers.AsNoTracking()
            .Where(m =>
                m.PlayerEntityId == player.Value
                && groupIds.Contains(m.GroupEntityId)
                && m.DeletedAt == null
            )
            .ToDictionaryAsync(m => m.GroupEntityId, m => m.ReadMessageCount, ct);

        Dictionary<int, int> threadCounts = await dbCtx
            .GroupForumThreads.AsNoTracking()
            .Where(t =>
                groupIds.Contains(t.GroupEntityId)
                && t.DeletedAt == null
                && t.State != GroupForumThreadState.Hidden
            )
            .GroupBy(t => t.GroupEntityId)
            .Select(g => new { GroupId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.GroupId, x => x.Count, ct);

        Dictionary<int, int> messageCounts = await dbCtx
            .GroupForumPosts.AsNoTracking()
            .Where(p =>
                groupIds.Contains(p.GroupEntityId)
                && p.DeletedAt == null
                && p.State == GroupForumPostState.Visible
            )
            .GroupBy(p => p.GroupEntityId)
            .Select(g => new { GroupId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.GroupId, x => x.Count, ct);

        List<ForumSnapshot> forums = groups
            .Select(g => new ForumSnapshot
            {
                GroupId = g.Id,
                Name = g.Name,
                Description = g.Description ?? string.Empty,
                Icon = g.Badge,
                TotalThreads = threadCounts.GetValueOrDefault(g.Id, 0),
                LeaderboardScore = 0,
                TotalMessages = messageCounts.GetValueOrDefault(g.Id, 0),
                UnreadMessages = Math.Max(
                    messageCounts.GetValueOrDefault(g.Id, 0)
                        - readCounts.GetValueOrDefault(g.Id, 0),
                    0
                ),
                LastMessageId = 0,
                LastMessageAuthorId = 0,
                LastMessageAuthorName = string.Empty,
                LastMessageTimeAsSecondsAgo = 0,
                // ForumsList only serializes the base fields; extended values are unused here.
                ReadPermissions = 0,
                PostMessagePermissions = 0,
                PostThreadPermissions = 0,
                ModeratePermissions = 0,
                ReadPermissionError = string.Empty,
                PostMessagePermissionError = string.Empty,
                PostThreadPermissionError = string.Empty,
                ModeratePermissionError = string.Empty,
                ReportPermissionError = string.Empty,
                CanChangeSettings = false,
                IsStaff = false,
            })
            .ToList();

        return new ForumsListPageSnapshot
        {
            ListCode = listCode,
            TotalAmount = totalAmount,
            StartIndex = startIndex,
            Forums = forums,
        };
    }

    public async Task<GuildFurniIdentitySnapshot?> GetFurniIdentityAsync(
        PlayerId player,
        int groupId,
        CancellationToken ct
    )
    {
        if (groupId <= 0)
        {
            return null;
        }

        await using VortexDbContext dbCtx = await dbCtxFactory.CreateDbContextAsync(ct);

        int playerId = player.Value;

        bool isMember = await dbCtx.GroupMembers.AnyAsync(
            m => m.GroupEntityId == groupId && m.PlayerEntityId == playerId && m.DeletedAt == null,
            ct
        );

        if (!isMember)
        {
            return null;
        }

        var group = await dbCtx
            .Groups.AsNoTracking()
            .Where(g => g.Id == groupId && g.DeletedAt == null)
            .Select(g => new
            {
                g.Badge,
                g.ColorOne,
                g.ColorTwo,
            })
            .FirstOrDefaultAsync(ct);

        if (group is null)
        {
            return null;
        }

        return new GuildFurniIdentitySnapshot
        {
            GroupId = groupId,
            BadgeCode = group.Badge,
            ColorOneHex = badgePartProvider.ResolveColorHex(group.ColorOne),
            ColorTwoHex = badgePartProvider.ResolveColorHex(group.ColorTwo),
        };
    }

    public async Task<int> GetUnreadForumsCountAsync(PlayerId player, CancellationToken ct)
    {
        await using VortexDbContext dbCtx = await dbCtxFactory.CreateDbContextAsync(ct);

        int playerId = player.Value;

        // Only forums of guilds the player belongs to can show up as unread in their friend bar.
        List<int> groupIds = await dbCtx
            .GroupMembers.AsNoTracking()
            .Where(m => m.PlayerEntityId == playerId && m.DeletedAt == null)
            .Select(m => m.GroupEntityId)
            .ToListAsync(ct);

        if (groupIds.Count == 0)
        {
            return 0;
        }

        Dictionary<int, int> visibleMessageCounts = await dbCtx
            .GroupForumPosts.AsNoTracking()
            .Where(p =>
                groupIds.Contains(p.GroupEntityId)
                && p.DeletedAt == null
                && p.State == GroupForumPostState.Visible
            )
            .GroupBy(p => p.GroupEntityId)
            .Select(g => new { GroupId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.GroupId, x => x.Count, ct);

        if (visibleMessageCounts.Count == 0)
        {
            return 0;
        }

        Dictionary<int, int> readCounts = await dbCtx
            .GroupForumReadMarkers.AsNoTracking()
            .Where(m =>
                m.PlayerEntityId == playerId
                && groupIds.Contains(m.GroupEntityId)
                && m.DeletedAt == null
            )
            .ToDictionaryAsync(m => m.GroupEntityId, m => m.ReadMessageCount, ct);

        return visibleMessageCounts.Count(entry =>
            entry.Value > readCounts.GetValueOrDefault(entry.Key, 0)
        );
    }

    public async Task UpdateForumReadMarkersAsync(
        PlayerId player,
        IReadOnlyList<ForumReadMarkerUpdate> markers,
        CancellationToken ct
    )
    {
        if (markers.Count == 0)
        {
            return;
        }

        await using VortexDbContext dbCtx = await dbCtxFactory.CreateDbContextAsync(ct);

        int playerId = player.Value;
        List<int> groupIds = [.. markers.Select(m => m.GroupId).Distinct()];

        // "Mark all read" must land on the server's own message count, not the client's — the
        // client's totalMessages can be stale, which would leave the forum stuck showing unread.
        Dictionary<int, int> serverMessageCounts = markers.Any(m => m.MarkAllRead)
            ? await dbCtx
                .GroupForumPosts.AsNoTracking()
                .Where(p =>
                    groupIds.Contains(p.GroupEntityId)
                    && p.DeletedAt == null
                    && p.State == GroupForumPostState.Visible
                )
                .GroupBy(p => p.GroupEntityId)
                .Select(g => new { GroupId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.GroupId, x => x.Count, ct)
            : [];

        List<GroupForumReadMarkerEntity> existing = await dbCtx
            .GroupForumReadMarkers.Where(m =>
                m.PlayerEntityId == playerId
                && groupIds.Contains(m.GroupEntityId)
                && m.DeletedAt == null
            )
            .ToListAsync(ct);

        Dictionary<int, GroupForumReadMarkerEntity> existingByGroup = existing.ToDictionary(m =>
            m.GroupEntityId
        );

        foreach (ForumReadMarkerUpdate update in markers)
        {
            int groupId = update.GroupId;
            int clamped = Math.Max(
                update.MarkAllRead
                    ? serverMessageCounts.GetValueOrDefault(groupId, update.ReadMessageCount)
                    : update.ReadMessageCount,
                0
            );

            if (existingByGroup.TryGetValue(groupId, out GroupForumReadMarkerEntity? marker))
            {
                // Never walk the marker backwards: the client sends per-view snapshots and an older
                // view arriving late would otherwise resurrect already-read messages as unread.
                marker.ReadMessageCount = Math.Max(marker.ReadMessageCount, clamped);
                continue;
            }

            dbCtx.GroupForumReadMarkers.Add(
                new GroupForumReadMarkerEntity
                {
                    GroupEntityId = groupId,
                    PlayerEntityId = playerId,
                    ReadMessageCount = clamped,
                    GroupEntity = null!,
                    PlayerEntity = null!,
                }
            );
        }

        await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);
    }

    /// <summary>
    ///     Assembles a deterministic guild badge code from the flattened part triples
    ///     (partId, colorId, position) the client submits. Exact glyph rendering depends on the
    ///     badge-parts config (the GuildEditorData feature); this preserves the player's selection.
    /// </summary>
    internal static string BuildBadgeCode(IReadOnlyList<int> parts)
    {
        if (parts.Count == 0)
        {
            return "b00000";
        }

        string code = string.Empty;
        for (int i = 0; i + 2 < parts.Count; i += 3)
        {
            code += $"b{parts[i]:D2}{parts[i + 1]:D2}{parts[i + 2]}";
        }

        return code.Length == 0 ? "b00000" : code;
    }
}
