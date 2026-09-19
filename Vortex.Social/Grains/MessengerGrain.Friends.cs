using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Orleans;
using Vortex.Database.Context;
using Vortex.Database.Entities.Messenger;
using Vortex.Database.Entities.Players;
using Vortex.Primitives.Events;
using Vortex.Primitives.FriendList.Enums;
using Vortex.Primitives.FriendList.Grains;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Server.Grains;
using Vortex.Primitives.Snapshots.FriendList;
using Vortex.Protocol.Messages.Outgoing.FriendList;
using Vortex.Social.Configuration;

namespace Vortex.Social.Grains;

internal sealed partial class MessengerGrain
{
    public Task<List<MessengerFriendSnapshot>> GetFriendsAsync(CancellationToken ct)
    {
        return Task.FromResult(_friends.Values.ToList());
    }

    public Task<List<FriendCategorySnapshot>> GetCategoriesAsync(CancellationToken ct)
    {
        return Task.FromResult(new List<FriendCategorySnapshot>(_categories));
    }

    public Task<List<int>> GetBlockedUserIdsAsync(CancellationToken ct)
    {
        return Task.FromResult(_blockedIds.ToList());
    }

    public Task<List<int>> GetIgnoredUserIdsAsync(CancellationToken ct)
    {
        return Task.FromResult(_ignoredIds.ToList());
    }

    public Task<List<FriendRequestSnapshot>> GetFriendRequestsAsync(CancellationToken ct)
    {
        return Task.FromResult(_incomingRequests.Values.ToList());
    }

    public Task<List<RelationshipStatusEntrySnapshot>> GetRelationshipStatusSummaryAsync(
        CancellationToken ct
    )
    {
        List<RelationshipStatusEntrySnapshot> grouped = _friends
            .Values.Where(f => f.RelationshipStatus != 0)
            .GroupBy(f => f.RelationshipStatus)
            .Select(g =>
            {
                MessengerFriendSnapshot first = g.First();
                return new RelationshipStatusEntrySnapshot
                {
                    RelationType = g.Key,
                    Count = g.Count(),
                    Name = first.Name,
                    Figure = first.Figure,
                    RandomFriendId = first.PlayerId.Value,
                };
            })
            .ToList();

        return Task.FromResult(grouped);
    }

    public Task<bool> IsFriendAsync(PlayerId targetId, CancellationToken ct)
    {
        return Task.FromResult(_friends.ContainsKey(targetId));
    }

    public Task<bool> HasBlockedUserAsync(PlayerId targetId, CancellationToken ct)
    {
        return Task.FromResult(_blockedIds.Contains(targetId.Value));
    }

    public Task<bool> HasIgnoredUserAsync(PlayerId targetId, CancellationToken ct)
    {
        return Task.FromResult(_ignoredIds.Contains(targetId.Value));
    }

    public async Task<FriendListErrorCodeType?> SendFriendRequestAsync(
        PlayerId targetId,
        string targetName,
        CancellationToken ct
    )
    {
        int maxFriends = await this
            .GrainFactory.GetServerConfigGrain()
            .GetIntAsync(MessengerConfig.MaxFriendsKey, MessengerConfig.MaxFriendsDefault)
            .ConfigureAwait(true);

        if (_friends.Count >= maxFriends)
        {
            return FriendListErrorCodeType.YouHitFriendLimit;
        }

        if (_friends.ContainsKey(targetId))
        {
            return null; // already friends, not an error per se
        }

        int playerId = SelfId;
        int targetIdInt = targetId;

        await using VortexDbContext dbCtx = await dbCtxFactory.CreateDbContextAsync(ct);

        int targetCountFriends = await dbCtx.MessengerFriends.CountAsync(
            f => f.PlayerEntityId == targetIdInt && f.DeletedAt == null,
            ct
        );

        if (targetCountFriends >= maxFriends)
        {
            return FriendListErrorCodeType.TheyHitFriendLimit;
        }

        bool alreadyExists = await dbCtx.MessengerRequests.AnyAsync(
            r =>
                r.PlayerEntityId == playerId
                && r.RequestedPlayerEntityId == targetIdInt
                && r.DeletedAt == null,
            ct
        );

        if (alreadyExists)
        {
            return null;
        }

        PlayerEntity selfEntity =
            await dbCtx.Players.FindAsync([playerId], ct)
            ?? throw new InvalidOperationException($"Player {playerId} not found");

        dbCtx.MessengerRequests.Add(
            new MessengerRequestEntity
            {
                PlayerEntityId = playerId,
                RequestedPlayerEntityId = targetIdInt,
                PlayerEntity = selfEntity,
                RequestedPlayerEntity = null!,
            }
        );

        await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);

        IMessengerGrain targetGrain = grainFactory.GetMessengerGrain(targetId);
        LogAndForget(
            targetGrain.ReceiveFriendRequestAsync(
                SelfId,
                selfEntity.Name,
                selfEntity.Figure,
                CancellationToken.None
            )
        );

        await events
            .PublishAsync(new FriendRequestSentEvent(playerId, targetIdInt), ct)
            .ConfigureAwait(true);

        return null;
    }

    public Task ReceiveFriendRequestAsync(
        PlayerId requesterId,
        string requesterName,
        string requesterFigure,
        CancellationToken ct
    )
    {
        _incomingRequests[requesterId.Value] = new FriendRequestSnapshot
        {
            RequestId = requesterId.Value,
            RequesterUserId = requesterId,
            RequesterName = requesterName,
            FigureString = requesterFigure,
        };

        IPlayerPresenceGrain presence = grainFactory.GetPlayerPresenceGrain(SelfId);
        LogAndForget(
            presence.SendComposerAsync(
                new NewFriendRequestMessageComposer
                {
                    Request = _incomingRequests[requesterId.Value],
                }
            )
        );

        return Task.CompletedTask;
    }

    public async Task<List<AcceptFriendFailureSnapshot>> AcceptFriendRequestsAsync(
        List<int> requesterIds,
        CancellationToken ct
    )
    {
        List<AcceptFriendFailureSnapshot> failures = new();

        int maxFriends = await this
            .GrainFactory.GetServerConfigGrain()
            .GetIntAsync(MessengerConfig.MaxFriendsKey, MessengerConfig.MaxFriendsDefault)
            .ConfigureAwait(true);

        await using VortexDbContext dbCtx = await dbCtxFactory.CreateDbContextAsync(ct);

        // Hoisted: the calling player is the same on every iteration.
        PlayerEntity? selfEntity = await dbCtx.Players.FindAsync([(int)SelfId], ct);

        // Resolved rather than assumed. Accepting is something a connected player does, but the
        // presence grain is the only thing entitled to answer that, and the value below is what the
        // requester's client is told about us -- assuming it is how both sides ended up filed as
        // offline the moment they became friends.
        bool selfOnline = await grainFactory
            .GetPlayerPresenceGrain(SelfId)
            .IsOnlineAsync(ct)
            .ConfigureAwait(true);

        foreach (int requesterId in requesterIds)
        {
            if (
                !_incomingRequests.TryGetValue(
                    requesterId,
                    out FriendRequestSnapshot? requestSnapshot
                )
            )
            {
                failures.Add(
                    new AcceptFriendFailureSnapshot
                    {
                        SenderId = PlayerId.Parse(requesterId),
                        ErrorCode = FriendListErrorCodeType.FriendRequestNotFound,
                    }
                );
                continue;
            }

            if (_friends.Count >= maxFriends)
            {
                failures.Add(
                    new AcceptFriendFailureSnapshot
                    {
                        SenderId = PlayerId.Parse(requesterId),
                        ErrorCode = FriendListErrorCodeType.YouHitFriendLimit,
                    }
                );
                continue;
            }

            PlayerEntity? friendEntity = await dbCtx.Players.FindAsync([requesterId], ct);

            if (selfEntity is null || friendEntity is null)
            {
                continue;
            }

            // Remove pending request through the change tracker so the delete and the
            // friendship inserts below commit atomically in one SaveChangesAsync.
            List<MessengerRequestEntity> pendingRequests = await dbCtx
                .MessengerRequests.Where(r =>
                    r.PlayerEntityId == requesterId
                    && r.RequestedPlayerEntityId == (int)SelfId
                    && r.DeletedAt == null
                )
                .ToListAsync(ct);

            dbCtx.MessengerRequests.RemoveRange(pendingRequests);

            // Create bidirectional friendship
            dbCtx.MessengerFriends.Add(
                new MessengerFriendEntity
                {
                    PlayerEntityId = SelfId,
                    FriendPlayerEntityId = requesterId,
                    RelationType = MessengerFriendRelationType.Zero,
                    PlayerEntity = selfEntity,
                    FriendPlayerEntity = friendEntity,
                }
            );

            dbCtx.MessengerFriends.Add(
                new MessengerFriendEntity
                {
                    PlayerEntityId = requesterId,
                    FriendPlayerEntityId = SelfId,
                    RelationType = MessengerFriendRelationType.Zero,
                    PlayerEntity = friendEntity,
                    FriendPlayerEntity = selfEntity,
                }
            );

            await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);

            await events
                .PublishAsync(new FriendRequestAcceptedEvent(SelfId, requesterId), ct)
                .ConfigureAwait(true);

            bool requesterOnline = await grainFactory
                .GetPlayerPresenceGrain(PlayerId.Parse(requesterId))
                .IsOnlineAsync(ct)
                .ConfigureAwait(true);

            // Update self in-memory
            _incomingRequests.Remove(requesterId);
            _friends[PlayerId.Parse(requesterId)] = new MessengerFriendSnapshot
            {
                PlayerId = PlayerId.Parse(requesterId),
                Name = friendEntity.Name,
                Gender = friendEntity.Gender,
                Online = requesterOnline,
                FollowingAllowed = true,
                Figure = friendEntity.Figure,
                CategoryId = 0,
                Motto = friendEntity.Motto ?? string.Empty,
                RealName = string.Empty,
                FacebookId = string.Empty,
                PersistedMessageUser = false,
                VipMember = false,
                PocketHabboUser = false,
                RelationshipStatus = 0,
            };

            // Notify friend's grain — fire-and-forget
            IMessengerGrain friendGrain = grainFactory.GetMessengerGrain(
                PlayerId.Parse(requesterId)
            );
            LogAndForget(
                friendGrain.NotifyFriendPresenceChangedAsync(
                    SelfId,
                    selfOnline,
                    selfEntity.Figure,
                    selfEntity.Motto ?? string.Empty,
                    CancellationToken.None
                )
            );
        }

        return failures;
    }

    public async Task DeclineFriendRequestsAsync(
        List<int> requesterIds,
        bool all,
        CancellationToken ct
    )
    {
        if (all)
        {
            _incomingRequests.Clear();

            await using VortexDbContext dbCtx = await dbCtxFactory.CreateDbContextAsync(ct);
            await dbCtx
                .MessengerRequests.Where(r =>
                    r.RequestedPlayerEntityId == (int)SelfId && r.DeletedAt == null
                )
                .ExecuteDeleteAsync(ct);

            await events
                .PublishAsync(new FriendRequestDeclinedEvent(SelfId, null), ct)
                .ConfigureAwait(true);

            return;
        }

        List<int> removedRequesterIds = requesterIds
            .Where(id => _incomingRequests.Remove(id))
            .ToList();

        if (removedRequesterIds.Count == 0)
        {
            return;
        }

        await using VortexDbContext dbCtxScoped = await dbCtxFactory.CreateDbContextAsync(ct);

        await dbCtxScoped
            .MessengerRequests.Where(r =>
                removedRequesterIds.Contains(r.PlayerEntityId)
                && r.RequestedPlayerEntityId == (int)SelfId
                && r.DeletedAt == null
            )
            .ExecuteDeleteAsync(ct);

        // One per requester: the person who was turned down is the one who might complain about it.
        foreach (int requesterId in removedRequesterIds)
        {
            await events
                .PublishAsync(new FriendRequestDeclinedEvent(SelfId, requesterId), ct)
                .ConfigureAwait(true);
        }
    }

    public async Task<List<int>> RemoveFriendsAsync(List<int> friendIds, CancellationToken ct)
    {
        List<int> removed = friendIds
            .Where(friendId => _friends.Remove(PlayerId.Parse(friendId)))
            .ToList();

        if (removed.Count == 0)
        {
            return removed;
        }

        int selfId = SelfId;

        await using VortexDbContext dbCtx = await dbCtxFactory.CreateDbContextAsync(ct);

        // Remove both directions for every removed friend in a single batched delete
        await dbCtx
            .MessengerFriends.Where(f =>
                (f.PlayerEntityId == selfId && removed.Contains(f.FriendPlayerEntityId))
                || (removed.Contains(f.PlayerEntityId) && f.FriendPlayerEntityId == selfId)
            )
            .ExecuteDeleteAsync(ct);

        List<Task> notifications = new(removed.Count);

        foreach (int friendId in removed)
        {
            notifications.Add(events.PublishAsync(new FriendRemovedEvent(SelfId, friendId), ct));

            // Notify friend's grain — fire-and-forget
            IMessengerGrain friendGrain = grainFactory.GetMessengerGrain(PlayerId.Parse(friendId));
            LogAndForget(friendGrain.NotifyFriendRemovedAsync(SelfId, CancellationToken.None));
        }

        await Task.WhenAll(notifications).ConfigureAwait(true);

        return removed;
    }

    public Task NotifyFriendRemovedAsync(PlayerId removedBy, CancellationToken ct)
    {
        _friends.Remove(removedBy);

        IPlayerPresenceGrain presence = grainFactory.GetPlayerPresenceGrain(SelfId);
        LogAndForget(
            presence.SendComposerAsync(
                new FriendListUpdateMessageComposer
                {
                    FriendCategories = [],
                    Updates =
                    [
                        new FriendListUpdateSnapshot
                        {
                            ActionType = FriendListUpdateActionType.Removed,
                            FriendId = removedBy.Value,
                            Friend = null,
                        },
                    ],
                }
            )
        );

        return Task.CompletedTask;
    }

    public async Task SetRelationshipStatusAsync(
        int friendId,
        MessengerFriendRelationType relationType,
        CancellationToken ct
    )
    {
        PlayerId friendKey = PlayerId.Parse(friendId);
        if (!_friends.TryGetValue(friendKey, out MessengerFriendSnapshot? existing))
        {
            return;
        }

        _friends[friendKey] = existing with { RelationshipStatus = (short)relationType };

        await using VortexDbContext dbCtx = await dbCtxFactory.CreateDbContextAsync(ct);
        await dbCtx
            .MessengerFriends.Where(f =>
                f.PlayerEntityId == (int)SelfId
                && f.FriendPlayerEntityId == friendId
                && f.DeletedAt == null
            )
            .ExecuteUpdateAsync(up => up.SetProperty(p => p.RelationType, relationType), ct)
            .ConfigureAwait(true);
    }

    public async Task BlockUserAsync(PlayerId targetId, CancellationToken ct)
    {
        if (_blockedIds.Contains(targetId.Value))
        {
            return;
        }

        _blockedIds.Add(targetId.Value);

        await using VortexDbContext dbCtx = await dbCtxFactory.CreateDbContextAsync(ct);

        PlayerEntity? selfEntity = await dbCtx.Players.FindAsync([(int)SelfId], ct);
        PlayerEntity? targetEntity = await dbCtx.Players.FindAsync([targetId.Value], ct);

        if (selfEntity is null || targetEntity is null)
        {
            return;
        }

        dbCtx.MessengerBlocked.Add(
            new MessengerBlockedEntity
            {
                PlayerEntityId = SelfId,
                BlockedPlayerEntityId = targetId.Value,
                PlayerEntity = selfEntity,
                BlockedPlayerEntity = targetEntity,
            }
        );

        await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);

        await events
            .PublishAsync(new UserBlockedEvent(SelfId, targetId.Value), ct)
            .ConfigureAwait(true);
    }

    public async Task UnblockUserAsync(PlayerId targetId, CancellationToken ct)
    {
        if (!_blockedIds.Remove(targetId.Value))
        {
            return;
        }

        await using VortexDbContext dbCtx = await dbCtxFactory.CreateDbContextAsync(ct);

        await dbCtx
            .MessengerBlocked.Where(b =>
                b.PlayerEntityId == (int)SelfId
                && b.BlockedPlayerEntityId == targetId.Value
                && b.DeletedAt == null
            )
            .ExecuteDeleteAsync(ct);

        await events
            .PublishAsync(new UserUnblockedEvent(SelfId, targetId.Value), ct)
            .ConfigureAwait(true);
    }

    public async Task IgnoreUserAsync(PlayerId targetId, CancellationToken ct)
    {
        if (_ignoredIds.Contains(targetId.Value))
        {
            return;
        }

        _ignoredIds.Add(targetId.Value);

        await using VortexDbContext dbCtx = await dbCtxFactory.CreateDbContextAsync(ct);

        PlayerEntity? selfEntity = await dbCtx.Players.FindAsync([(int)SelfId], ct);
        PlayerEntity? targetEntity = await dbCtx.Players.FindAsync([targetId.Value], ct);

        if (selfEntity is null || targetEntity is null)
        {
            return;
        }

        dbCtx.MessengerIgnored.Add(
            new MessengerIgnoredEntity
            {
                PlayerEntityId = SelfId,
                IgnoredPlayerEntityId = targetId.Value,
                PlayerEntity = selfEntity,
                IgnoredPlayerEntity = targetEntity,
            }
        );

        await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);
    }

    public async Task UnignoreUserAsync(PlayerId targetId, CancellationToken ct)
    {
        if (!_ignoredIds.Remove(targetId.Value))
        {
            return;
        }

        await using VortexDbContext dbCtx = await dbCtxFactory.CreateDbContextAsync(ct);

        await dbCtx
            .MessengerIgnored.Where(i =>
                i.PlayerEntityId == (int)SelfId
                && i.IgnoredPlayerEntityId == targetId.Value
                && i.DeletedAt == null
            )
            .ExecuteDeleteAsync(ct);
    }

    public Task<List<MessengerSearchResultSnapshot>> GetFriendSearchResultsAsync(
        string query,
        CancellationToken ct
    )
    {
        string lower = query.ToLowerInvariant();

        List<MessengerSearchResultSnapshot> results = _friends
            .Values.Where(f => f.Name.Contains(lower, StringComparison.OrdinalIgnoreCase))
            .Select(f => new MessengerSearchResultSnapshot
            {
                PlayerId = f.PlayerId,
                Name = f.Name,
                Motto = f.Motto,
                Online = f.Online,
                FollowingAllowed = f.FollowingAllowed,
                UnknownString = string.Empty,
                Gender = f.Gender,
                Figure = f.Figure,
                RealName = f.RealName,
            })
            .ToList();

        return Task.FromResult(results);
    }
}
