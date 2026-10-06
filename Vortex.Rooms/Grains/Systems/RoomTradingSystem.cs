using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Vortex.Database.Context;
using Vortex.Events.Registry;
using Vortex.Primitives.Action;
using Vortex.Primitives.Events;
using Vortex.Primitives.Inventory.Grains;
using Vortex.Primitives.Inventory.Snapshots;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object;
using Vortex.Protocol.Messages.Outgoing.Inventory.Trading;
using Vortex.Rooms.Grains;

namespace Vortex.Rooms.Grains.Systems;

/// <summary>
/// The player-to-player furniture trade flow. Kept on the room grain rather than a separate grain:
/// opening and offer-building need the room's live avatar/object-id mapping
/// (<c>_state.AvatarsByPlayerId</c>) on every step, and a leaving player's trade is torn down
/// synchronously from <c>RoomGrain.Avatar.cs</c> as part of the same turn that removes their avatar.
/// </summary>
public sealed class RoomTradingSystem(RoomGrain roomGrain)
{
    // Client TradeOpenFailed reasons 7/8 pop a generic alert; any other value shows the templated
    // "could not trade with {name}" message, which fits every gating failure here.
    private const int TradeOpenFailedReason = 6;

    // TradingClose reason 1 is the client's commit-error alert; 0 is an ordinary cancellation.
    private const int TradeCloseCancelled = 0;
    private const int TradeCloseCommitError = 1;

    private readonly RoomGrain _roomGrain = roomGrain;

    public async Task OpenTradeAsync(
        PlayerId requesterId,
        int otherRoomObjectId,
        CancellationToken ct
    )
    {
        if (!_roomGrain._state.AvatarsByPlayerId.ContainsKey(requesterId))
        {
            return;
        }

        if (
            !TryResolvePlayerByObjectId(otherRoomObjectId, out PlayerId otherId)
            || otherId == requesterId
        )
        {
            await SendTradeOpenFailedAsync(requesterId, otherId, ct).ConfigureAwait(true);
            return;
        }

        if (
            _roomGrain._state.TradeSessionsByPlayerId.ContainsKey(requesterId)
            || _roomGrain._state.TradeSessionsByPlayerId.ContainsKey(otherId)
        )
        {
            await SendTradeOpenFailedAsync(requesterId, otherId, ct).ConfigureAwait(true);
            return;
        }

        bool requesterCanTrade = await CanTradeInRoomAsync(requesterId, ct).ConfigureAwait(true);
        bool otherCanTrade = await CanTradeInRoomAsync(otherId, ct).ConfigureAwait(true);

        if (!requesterCanTrade || !otherCanTrade)
        {
            await SendTradeOpenFailedAsync(requesterId, otherId, ct).ConfigureAwait(true);
            return;
        }

        RoomObjectId requesterObjectId = _roomGrain._state.AvatarsByPlayerId[requesterId];
        RoomObjectId otherObjectId = _roomGrain._state.AvatarsByPlayerId[otherId];

        RoomTradeSession session = new()
        {
            UserOneId = requesterId,
            UserTwoId = otherId,
            UserOneObjectId = requesterObjectId,
            UserTwoObjectId = otherObjectId,
        };

        _roomGrain._state.TradeSessionsByPlayerId[requesterId] = session;
        _roomGrain._state.TradeSessionsByPlayerId[otherId] = session;

        // Player ids, not room-object ids -- see the note on TradingOpenEventMessageComposer.
        await SendToTradeAsync(
                session,
                new TradingOpenEventMessageComposer
                {
                    UserId = requesterId.Value,
                    UserCanTrade = true,
                    OtherUserId = otherId.Value,
                    OtherUserCanTrade = true,
                }
            )
            .ConfigureAwait(true);

        await _roomGrain
            ._events.PublishAsync(
                new TradeStartedEvent(
                    requesterId.Value,
                    otherId.Value,
                    _roomGrain._state.RoomId.Value
                ),
                ct
            )
            .ConfigureAwait(true);
    }

    public async Task AddTradeItemsAsync(
        PlayerId requesterId,
        IReadOnlyList<int> itemIds,
        CancellationToken ct
    )
    {
        if (!TryGetSession(requesterId, out RoomTradeSession? session))
        {
            await SendTradingNotOpenAsync(requesterId, ct).ConfigureAwait(true);
            return;
        }

        if (session.Phase != TradePhase.Building)
        {
            return;
        }

        List<int> offer = session.ItemsOf(requesterId);
        IInventoryGrain inventory = _roomGrain._grainFactory.GetInventoryGrain(requesterId);
        bool changed = false;

        foreach (int itemId in itemIds)
        {
            if (
                offer.Contains(itemId)
                || offer.Count >= _roomGrain._roomConfig.MaxTradeItemsPerSide
            )
            {
                continue;
            }

            FurnitureItemSnapshot? snapshot = await inventory
                .GetItemSnapshotAsync(itemId, ct)
                .ConfigureAwait(true);

            // An item the requester no longer owns or that is flagged non-tradeable is silently
            // dropped from the offer -- there is no distinct "rejected item" composer in this client,
            // and it will simply be absent from the item list the other party sees.
            if (snapshot is null || !snapshot.Definition.CanTrade)
            {
                continue;
            }

            offer.Add(itemId);
            changed = true;
        }

        if (changed)
        {
            session.ResetAgreement();
            await BroadcastItemListAsync(session, ct).ConfigureAwait(true);
        }
    }

    public async Task RemoveTradeItemAsync(PlayerId requesterId, int itemId, CancellationToken ct)
    {
        if (!TryGetSession(requesterId, out RoomTradeSession? session))
        {
            return;
        }

        if (session.Phase != TradePhase.Building)
        {
            return;
        }

        if (!session.ItemsOf(requesterId).Remove(itemId))
        {
            return;
        }

        session.ResetAgreement();
        await BroadcastItemListAsync(session, ct).ConfigureAwait(true);
    }

    public async Task SetTradeAcceptAsync(PlayerId requesterId, bool accepted, CancellationToken ct)
    {
        if (!TryGetSession(requesterId, out RoomTradeSession? session))
        {
            return;
        }

        if (session.Phase != TradePhase.Building || session.AcceptedOf(requesterId) == accepted)
        {
            return;
        }

        session.SetAccepted(requesterId, accepted);

        await SendToTradeAsync(
                session,
                new TradingAcceptEventMessageComposer
                {
                    UserId = requesterId.Value,
                    UserAccepts = accepted,
                }
            )
            .ConfigureAwait(true);

        if (session.BothAccepted)
        {
            session.Phase = TradePhase.Confirming;

            await SendToTradeAsync(session, new TradingConfirmationEventMessageComposer())
                .ConfigureAwait(true);
        }
    }

    public async Task ConfirmTradeAsync(PlayerId requesterId, bool confirm, CancellationToken ct)
    {
        if (!TryGetSession(requesterId, out RoomTradeSession? session))
        {
            return;
        }

        if (session.Phase != TradePhase.Confirming)
        {
            return;
        }

        if (!confirm)
        {
            await EndTradeAsync(
                    session,
                    requesterId,
                    TradeCloseCancelled,
                    "declined",
                    [session.UserOneId, session.UserTwoId],
                    ct
                )
                .ConfigureAwait(true);
            return;
        }

        session.SetConfirmed(requesterId, true);

        if (session.BothConfirmed)
        {
            await CommitTradeAsync(session, ct).ConfigureAwait(true);
        }
    }

    public async Task CloseTradeAsync(PlayerId requesterId, CancellationToken ct)
    {
        if (!TryGetSession(requesterId, out RoomTradeSession? session))
        {
            return;
        }

        await EndTradeAsync(
                session,
                requesterId,
                TradeCloseCancelled,
                "cancelled",
                [session.UserOneId, session.UserTwoId],
                ct
            )
            .ConfigureAwait(true);
    }

    /// <summary>Tears down any trade a leaving/disconnecting player is in, notifying the participant
    /// who is still present. Called from <c>RoomGrain.Avatar.cs</c> when an avatar is removed.</summary>
    public async Task CloseTradeForLeavingPlayerAsync(PlayerId playerId, CancellationToken ct)
    {
        if (!TryGetSession(playerId, out RoomTradeSession? session))
        {
            return;
        }

        PlayerId remaining = session.OtherOf(playerId);

        await EndTradeAsync(session, playerId, TradeCloseCancelled, "left", [remaining], ct)
            .ConfigureAwait(true);
    }

    private async Task CommitTradeAsync(RoomTradeSession session, CancellationToken ct)
    {
        bool swapped = false;

        // Before the re-validation, so a refusal costs nothing and unwinds through the path that
        // already exists for "the offer no longer holds": both sides get their items back.
        EventContext completing = await _roomGrain
            ._cancellableEvents.PublishCancellableAsync(
                new TradeCompletingEvent(
                    session.UserOneId.Value,
                    session.UserTwoId.Value,
                    [.. session.UserOneItemIds],
                    [.. session.UserTwoItemIds],
                    _roomGrain._state.RoomId.Value
                ),
                ct
            )
            .ConfigureAwait(true);

        if (completing.Cancel)
        {
            _roomGrain._logger.LogInformation(
                "Trade between {UserOne} and {UserTwo} in room {RoomId} was cancelled: {Reason}",
                session.UserOneId,
                session.UserTwoId,
                _roomGrain._state.RoomId,
                completing.CancelReason ?? string.Empty
            );

            await AbortCommitAsync(session, ct).ConfigureAwait(true);

            return;
        }

        try
        {
            // Re-validate ownership and tradeability right before committing: an item may have been
            // placed, sold, or traded elsewhere while the confirm dialog was open.
            if (
                !await ValidateOfferAsync(session.UserOneId, session.UserOneItemIds, ct)
                    .ConfigureAwait(true)
                || !await ValidateOfferAsync(session.UserTwoId, session.UserTwoItemIds, ct)
                    .ConfigureAwait(true)
            )
            {
                await AbortCommitAsync(session, ct).ConfigureAwait(true);
                return;
            }

            swapped = await TryPersistOwnershipSwapAsync(session, ct).ConfigureAwait(true);

            if (!swapped)
            {
                await AbortCommitAsync(session, ct).ConfigureAwait(true);
                return;
            }
        }
        catch (System.Exception ex)
        {
            _roomGrain._logger.LogError(
                ex,
                "Trade commit failed between {UserOne} and {UserTwo} in room {RoomId}.",
                session.UserOneId,
                session.UserTwoId,
                _roomGrain._state.RoomId
            );

            if (!swapped)
            {
                await AbortCommitAsync(session, ct).ConfigureAwait(true);
                return;
            }
        }

        int[] oneItems = [.. session.UserOneItemIds];
        int[] twoItems = [.. session.UserTwoItemIds];

        RemoveSession(session);

        // Ownership is persisted; the rest is best-effort resync/notification. A stale inventory
        // cache self-heals on the next reload, so a failure here can never duplicate or lose an item.
        try
        {
            await _roomGrain
                ._grainFactory.GetInventoryGrain(session.UserOneId)
                .ApplyTradeResultAsync(oneItems, twoItems, ct)
                .ConfigureAwait(true);
            await _roomGrain
                ._grainFactory.GetInventoryGrain(session.UserTwoId)
                .ApplyTradeResultAsync(twoItems, oneItems, ct)
                .ConfigureAwait(true);

            // Per-item forensics (item_events) plus one trade-level business-audit record that the
            // dashboard's Item audit surface reads.
            await PublishTradeAuditAsync(session.UserOneId, session.UserTwoId, oneItems, ct)
                .ConfigureAwait(true);
            await PublishTradeAuditAsync(session.UserTwoId, session.UserOneId, twoItems, ct)
                .ConfigureAwait(true);
            await _roomGrain
                ._events.PublishAsync(
                    new TradeCompletedEvent(
                        session.UserOneId.Value,
                        session.UserTwoId.Value,
                        oneItems,
                        twoItems,
                        _roomGrain._state.RoomId.Value
                    ),
                    ct
                )
                .ConfigureAwait(true);
        }
        catch (System.Exception ex)
        {
            _roomGrain._logger.LogWarning(
                ex,
                "Trade committed but post-commit resync failed in room {RoomId}.",
                _roomGrain._state.RoomId
            );
        }

        await Task.WhenAll(
                _roomGrain
                    ._grainFactory.GetPlayerPresenceGrain(session.UserOneId)
                    .SendComposerAsync(new TradingCompletedEventMessageComposer()),
                _roomGrain
                    ._grainFactory.GetPlayerPresenceGrain(session.UserTwoId)
                    .SendComposerAsync(new TradingCompletedEventMessageComposer())
            )
            .ConfigureAwait(true);
    }

    private async Task<bool> ValidateOfferAsync(
        PlayerId playerId,
        IReadOnlyList<int> itemIds,
        CancellationToken ct
    )
    {
        if (itemIds.Count == 0)
        {
            return true;
        }

        IInventoryGrain inventory = _roomGrain._grainFactory.GetInventoryGrain(playerId);

        foreach (int itemId in itemIds)
        {
            FurnitureItemSnapshot? snapshot = await inventory
                .GetItemSnapshotAsync(itemId, ct)
                .ConfigureAwait(true);

            if (snapshot is null || !snapshot.Definition.CanTrade)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Swaps furniture ownership for both offers inside a single database transaction, so the
    /// exchange is all-or-nothing: either every item changes hands or none does. Guards that each row
    /// is still owned by its offerer and not placed in a room before flipping <c>player_id</c>.</summary>
    private async Task<bool> TryPersistOwnershipSwapAsync(
        RoomTradeSession session,
        CancellationToken ct
    )
    {
        List<int> oneIds = session.UserOneItemIds;
        List<int> twoIds = session.UserTwoItemIds;
        int oneOwner = session.UserOneId.Value;
        int twoOwner = session.UserTwoId.Value;

        // A fresh DbContext is created per attempt (rather than shared across retries) so that a
        // transient failure retried by the MySQL execution strategy never resubmits entities left
        // half-tracked by a rolled-back attempt.
        await using VortexDbContext strategyProbe = await _roomGrain
            ._dbCtxFactory.CreateDbContextAsync(ct)
            .ConfigureAwait(true);
        IExecutionStrategy strategy = strategyProbe.Database.CreateExecutionStrategy();

        return await strategy
            .ExecuteAsync(async () =>
            {
                await using VortexDbContext dbCtx = await _roomGrain
                    ._dbCtxFactory.CreateDbContextAsync(ct)
                    .ConfigureAwait(true);
                await using IDbContextTransaction tx = await dbCtx
                    .Database.BeginTransactionAsync(ct)
                    .ConfigureAwait(true);

                // Claimed, not read and then written. The check and the write are one statement, so
                // two trades of the same chair cannot both read it as its owner's and both hand it
                // over: the second updates nothing and its count comes back short (ECON-ITM-004).
                // This is the shape the marketplace has always used; the trade is the
                // last of the five ownership paths that did not.
                //
                // The chest predicate is new. An item staked in a wired chest keeps its player_id
                // exactly as a loose one does, so the old guard -- owner, and not in a room -- would
                // trade away somebody's shop stock.
                int oneMoved = await ClaimForTradeAsync(dbCtx, oneIds, oneOwner, twoOwner, ct)
                    .ConfigureAwait(true);
                int twoMoved = await ClaimForTradeAsync(dbCtx, twoIds, twoOwner, oneOwner, ct)
                    .ConfigureAwait(true);

                if (oneMoved != oneIds.Count || twoMoved != twoIds.Count)
                {
                    // Nothing is committed, so the transaction takes both halves back with it.
                    return false;
                }

                await tx.CommitAsync(ct).ConfigureAwait(true);

                return true;
            })
            .ConfigureAwait(true);
    }

    /// <summary>
    /// Hands one side's furniture over, and answers how many rows it actually took.
    /// </summary>
    /// <remarks>
    /// The predicate is the definition of "this player's, and free to move": theirs, not standing in
    /// a room, not staked in a wired chest, not loaded into a jukebox, not deleted. A row that fails
    /// any of it is not updated and the caller sees a count that does not match what was offered.
    /// </remarks>
    private static Task<int> ClaimForTradeAsync(
        VortexDbContext dbCtx,
        List<int> itemIds,
        int fromOwner,
        int toOwner,
        CancellationToken ct
    ) =>
        itemIds.Count == 0
            ? Task.FromResult(0)
            : dbCtx
                .Furnitures.Where(f =>
                    itemIds.Contains(f.Id)
                    && f.PlayerEntityId == fromOwner
                    && f.RoomEntityId == null
                    && f.WiredChestEntityId == null
                    && f.JukeboxEntityId == null
                    && f.DeletedAt == null
                )
                .ExecuteUpdateAsync(row => row.SetProperty(f => f.PlayerEntityId, toOwner), ct);

    private async Task PublishTradeAuditAsync(
        PlayerId fromOwner,
        PlayerId toOwner,
        int[] itemIds,
        CancellationToken ct
    )
    {
        foreach (int itemId in itemIds)
        {
            await _roomGrain
                ._events.PublishAsync(
                    new ItemTradedEvent(
                        itemId,
                        fromOwner.Value,
                        fromOwner.Value,
                        toOwner.Value,
                        _roomGrain._state.RoomId.Value
                    ),
                    ct
                )
                .ConfigureAwait(true);
        }
    }

    private async Task AbortCommitAsync(RoomTradeSession session, CancellationToken ct)
    {
        await EndTradeAsync(
                session,
                session.UserOneId,
                TradeCloseCommitError,
                "commit_error",
                [session.UserOneId, session.UserTwoId],
                ct
            )
            .ConfigureAwait(true);
    }

    private async Task EndTradeAsync(
        RoomTradeSession session,
        PlayerId cancellerId,
        int reason,
        string auditReason,
        IReadOnlyList<PlayerId> notify,
        CancellationToken ct
    )
    {
        PlayerId userOne = session.UserOneId;
        PlayerId userTwo = session.UserTwoId;

        RemoveSession(session);

        TradingCloseEventMessageComposer composer = new()
        {
            UserId = cancellerId.Value,
            Reason = reason,
        };

        await Task.WhenAll(
                notify.Select(id =>
                    _roomGrain._grainFactory.GetPlayerPresenceGrain(id).SendComposerAsync(composer)
                )
            )
            .ConfigureAwait(true);

        await _roomGrain
            ._events.PublishAsync(
                new TradeCancelledEvent(
                    userOne.Value,
                    userTwo.Value,
                    _roomGrain._state.RoomId.Value,
                    auditReason
                ),
                ct
            )
            .ConfigureAwait(true);
    }

    private void RemoveSession(RoomTradeSession session)
    {
        _roomGrain._state.TradeSessionsByPlayerId.Remove(session.UserOneId);
        _roomGrain._state.TradeSessionsByPlayerId.Remove(session.UserTwoId);
    }

    private bool TryGetSession(PlayerId playerId, out RoomTradeSession session)
    {
        if (
            _roomGrain._state.TradeSessionsByPlayerId.TryGetValue(
                playerId,
                out RoomTradeSession? found
            ) && found.IsParticipant(playerId)
        )
        {
            session = found;
            return true;
        }

        session = null!;
        return false;
    }

    private bool TryResolvePlayerByObjectId(int objectId, out PlayerId playerId)
    {
        foreach (
            (PlayerId candidate, RoomObjectId candidateObjectId) in _roomGrain
                ._state
                .AvatarsByPlayerId
        )
        {
            if (candidateObjectId.Value == objectId)
            {
                playerId = candidate;
                return true;
            }
        }

        playerId = default;
        return false;
    }

    private async Task<bool> CanTradeInRoomAsync(PlayerId playerId, CancellationToken ct)
    {
        RoomTradeModeType mode = _roomGrain._state.RoomSnapshot.TradeType;

        if (mode == RoomTradeModeType.Disabled)
        {
            return false;
        }

        if (mode == RoomTradeModeType.Everyone)
        {
            return true;
        }

        RoomControllerType level = await _roomGrain
            .GetControllerLevelAsync(
                ActionContext.CreateForPlayer(playerId, _roomGrain._state.RoomId),
                ct
            )
            .ConfigureAwait(true);

        return level >= RoomControllerType.Rights;
    }

    private async Task SendTradeOpenFailedAsync(
        PlayerId requesterId,
        PlayerId otherId,
        CancellationToken ct
    )
    {
        string otherName =
            otherId > 0
                ? await _roomGrain
                    ._grainFactory.GetPlayerDirectoryGrain()
                    .GetPlayerNameAsync(otherId, ct)
                    .ConfigureAwait(true)
                : string.Empty;

        await _roomGrain
            ._grainFactory.GetPlayerPresenceGrain(requesterId)
            .SendComposerAsync(
                new TradeOpenFailedEventPaserMessageComposer
                {
                    Reason = TradeOpenFailedReason,
                    OtherUserName = otherName,
                }
            )
            .ConfigureAwait(true);
    }

    private Task SendTradingNotOpenAsync(PlayerId playerId, CancellationToken ct) =>
        _roomGrain
            ._grainFactory.GetPlayerPresenceGrain(playerId)
            .SendComposerAsync(new TradingNotOpenEventMessageComposer());

    private async Task BroadcastItemListAsync(RoomTradeSession session, CancellationToken ct)
    {
        ImmutableArray<FurnitureItemSnapshot> oneItems = await ResolveOfferSnapshotsAsync(
                session.UserOneId,
                session.UserOneItemIds,
                ct
            )
            .ConfigureAwait(true);
        ImmutableArray<FurnitureItemSnapshot> twoItems = await ResolveOfferSnapshotsAsync(
                session.UserTwoId,
                session.UserTwoItemIds,
                ct
            )
            .ConfigureAwait(true);

        await SendToTradeAsync(
                session,
                new TradingItemListEventMessageComposer
                {
                    FirstUserId = session.UserOneId.Value,
                    FirstUserItems = oneItems,
                    FirstUserCredits = 0,
                    SecondUserId = session.UserTwoId.Value,
                    SecondUserItems = twoItems,
                    SecondUserCredits = 0,
                }
            )
            .ConfigureAwait(true);
    }

    private async Task<ImmutableArray<FurnitureItemSnapshot>> ResolveOfferSnapshotsAsync(
        PlayerId playerId,
        List<int> itemIds,
        CancellationToken ct
    )
    {
        if (itemIds.Count == 0)
        {
            return [];
        }

        IInventoryGrain inventory = _roomGrain._grainFactory.GetInventoryGrain(playerId);
        ImmutableArray<FurnitureItemSnapshot>.Builder builder =
            ImmutableArray.CreateBuilder<FurnitureItemSnapshot>(itemIds.Count);

        foreach (int itemId in itemIds)
        {
            FurnitureItemSnapshot? snapshot = await inventory
                .GetItemSnapshotAsync(itemId, ct)
                .ConfigureAwait(true);

            if (snapshot is not null)
            {
                builder.Add(snapshot);
            }
        }

        return builder.ToImmutable();
    }

    private Task SendToTradeAsync(RoomTradeSession session, IComposer composer) =>
        Task.WhenAll(
            _roomGrain
                ._grainFactory.GetPlayerPresenceGrain(session.UserOneId)
                .SendComposerAsync(composer),
            _roomGrain
                ._grainFactory.GetPlayerPresenceGrain(session.UserTwoId)
                .SendComposerAsync(composer)
        );
}
