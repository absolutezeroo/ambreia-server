using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Orleans.Concurrency;
using Vortex.Primitives.FriendList.Enums;
using Vortex.Primitives.Players;
using Vortex.Primitives.Snapshots.FriendList;

namespace Vortex.Primitives.FriendList.Grains;

public partial interface IMessengerGrain : IGrainWithIntegerKey
{
    // ── Reads ────────────────────────────────────────────────────────────────
    Task<List<MessengerFriendSnapshot>> GetFriendsAsync(CancellationToken ct);
    Task<List<FriendCategorySnapshot>> GetCategoriesAsync(CancellationToken ct);
    Task<List<int>> GetBlockedUserIdsAsync(CancellationToken ct);
    Task<List<int>> GetIgnoredUserIdsAsync(CancellationToken ct);
    Task<List<FriendRequestSnapshot>> GetFriendRequestsAsync(CancellationToken ct);
    Task<List<RelationshipStatusEntrySnapshot>> GetRelationshipStatusSummaryAsync(
        CancellationToken ct
    );

    // ── Predicate helpers ────────────────────────────────────────────────────
    Task<bool> IsFriendAsync(PlayerId targetId, CancellationToken ct);
    Task<bool> HasBlockedUserAsync(PlayerId targetId, CancellationToken ct);
    Task<bool> HasIgnoredUserAsync(PlayerId targetId, CancellationToken ct);

    // ── Friend request lifecycle ─────────────────────────────────────────────
    Task<FriendListErrorCodeType?> SendFriendRequestAsync(
        PlayerId targetId,
        string targetName,
        CancellationToken ct
    );
    Task ReceiveFriendRequestAsync(
        PlayerId requesterId,
        string requesterName,
        string requesterFigure,
        CancellationToken ct
    );
    Task<List<AcceptFriendFailureSnapshot>> AcceptFriendRequestsAsync(
        List<int> requesterIds,
        CancellationToken ct
    );
    Task DeclineFriendRequestsAsync(List<int> requesterIds, bool all, CancellationToken ct);

    // ── Friendship mutations ─────────────────────────────────────────────────
    Task<List<int>> RemoveFriendsAsync(List<int> friendIds, CancellationToken ct);
    Task NotifyFriendRemovedAsync(PlayerId removedBy, CancellationToken ct);
    Task SetRelationshipStatusAsync(
        int friendId,
        MessengerFriendRelationType relationType,
        CancellationToken ct
    );

    // ── Block / ignore ───────────────────────────────────────────────────────
    Task BlockUserAsync(PlayerId targetId, CancellationToken ct);
    Task UnblockUserAsync(PlayerId targetId, CancellationToken ct);
    Task IgnoreUserAsync(PlayerId targetId, CancellationToken ct);
    Task UnignoreUserAsync(PlayerId targetId, CancellationToken ct);

    // ── Messaging ────────────────────────────────────────────────────────────
    /// <summary>
    /// Sends one private message. Set <paramref name="habbiconId"/> to make it a Habbicon rather
    /// than a line of text — the client models a Habbicon in a conversation as a message of its own,
    /// so it takes the same path and the same friend, block and persistence rules rather than a
    /// parallel one that could differ from them.
    /// </summary>
    Task<InstantMessageErrorCodeType?> SendMessageAsync(
        PlayerId receiverId,
        string message,
        int chatId,
        int confirmationId,
        int habbiconId,
        CancellationToken ct
    );

    /// <summary>
    /// The receiving half of <see cref="SendMessageAsync"/>, which awaits it.
    /// </summary>
    /// <remarks>
    /// Interleaved because the sender's turn is held open across this call: two friends writing to
    /// each other at the same moment each held their own turn while waiting for the other's, and
    /// both stood still until the 30 s call timeout. Safe to run inside another turn because it
    /// never awaits: it reads the ignore list, hands the push to presence fire-and-forget, and adds
    /// an id to the delivered set, which the flush copies and clears in one synchronous step.
    /// </remarks>
    [AlwaysInterleave]
    Task ReceiveMessageAsync(
        PlayerId senderId,
        string senderName,
        string senderFigure,
        string message,
        DateTime timestamp,
        int messageId,
        int habbiconId,
        CancellationToken ct
    );
    Task<List<MessageHistoryEntrySnapshot>> GetMessageHistoryAsync(
        PlayerId friendId,
        CancellationToken ct
    );

    // ── Presence notifications ───────────────────────────────────────────────
    Task NotifyOnlineAsync(CancellationToken ct);
    Task NotifyOfflineAsync(CancellationToken ct);
    Task NotifyFriendPresenceChangedAsync(
        PlayerId friendId,
        bool online,
        string figure,
        string motto,
        CancellationToken ct
    );

    // ── Room actions ─────────────────────────────────────────────────────────
    Task ReceiveRoomInviteAsync(PlayerId senderId, string message, CancellationToken ct);

    // ── Search ───────────────────────────────────────────────────────────────
    Task<List<MessengerSearchResultSnapshot>> GetFriendSearchResultsAsync(
        string query,
        CancellationToken ct
    );
}
