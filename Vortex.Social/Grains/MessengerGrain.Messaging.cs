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
    public async Task<InstantMessageErrorCodeType?> SendMessageAsync(
        PlayerId receiverId,
        string message,
        int chatId,
        int confirmationId,
        int habbiconId,
        CancellationToken ct
    )
    {
        if (!_friends.ContainsKey(receiverId))
        {
            return InstantMessageErrorCodeType.NotFriend;
        }

        if (_blockedIds.Contains(receiverId.Value))
        {
            return InstantMessageErrorCodeType.ReceiverMuted;
        }

        DateTime now = DateTime.UtcNow;

        await using VortexDbContext dbCtx = await dbCtxFactory.CreateDbContextAsync(ct);

        PlayerEntity? selfEntity = await dbCtx.Players.FindAsync([(int)SelfId], ct);
        PlayerEntity? receiverEntity = await dbCtx.Players.FindAsync([receiverId.Value], ct);

        if (selfEntity is null || receiverEntity is null)
        {
            return InstantMessageErrorCodeType.Offline;
        }

        MessengerMessageEntity msgEntity = new()
        {
            SenderEntityId = SelfId,
            ReceiverEntityId = receiverId.Value,
            Message = message,
            HabbiconId = habbiconId,
            Timestamp = now,
            Delivered = false,
            SenderEntity = selfEntity,
            ReceiverEntity = receiverEntity,
        };

        dbCtx.MessengerMessages.Add(msgEntity);
        await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);

        // Awaited, not fire-and-forget. The receiver decides for itself whether it has a socket to
        // push to, so nothing here is waiting on a network round trip to a client -- but the call
        // has to be allowed to fail loudly. Dropped on the floor, a receiver grain that cannot be
        // reached (activation throwing in HydrateAsync, an unhandled fault in the hop) takes the
        // message with it: ReceiveMessageAsync never runs, so it never reaches the socket AND its
        // id never enters _pendingDeliveredIds, leaving the row Delivered = false. The player then
        // sees nothing -- no message, no sound, no unread marker -- until the next login, where
        // DeliverOfflinePendingMessagesAsync replays the whole conversation at once. That is
        // exactly the "I only get my messages when I reconnect" report this came from, and the
        // swallowed exception is why it left no trace anywhere.
        IMessengerGrain receiverGrain = grainFactory.GetMessengerGrain(receiverId);

        await receiverGrain
            .ReceiveMessageAsync(
                SelfId,
                selfEntity.Name,
                selfEntity.Figure,
                message,
                now,
                msgEntity.Id,
                habbiconId,
                CancellationToken.None
            )
            .ConfigureAwait(true);

        // Accepted and on its way. Raised after the friend and block rules rather than when the
        // packet arrived, so a message either of them refused is not one anything can reward.
        await events
            .PublishAsync(
                new MessengerMessageSentEvent(SelfId, receiverId, habbiconId),
                CancellationToken.None
            )
            .ConfigureAwait(true);

        return null;
    }

    public Task ReceiveMessageAsync(
        PlayerId senderId,
        string senderName,
        string senderFigure,
        string message,
        DateTime timestamp,
        int messageId,
        int habbiconId,
        CancellationToken ct
    )
    {
        if (_ignoredIds.Contains(senderId.Value))
        {
            return Task.CompletedTask;
        }

        int secondsSinceSent = (int)(DateTime.UtcNow - timestamp).TotalSeconds;

        IPlayerPresenceGrain presence = grainFactory.GetPlayerPresenceGrain(SelfId);
        LogAndForget(
            presence.SendComposerAsync(
                new NewConsoleMessageMessageComposer
                {
                    ChatId = senderId.Value,
                    Message = message,
                    HabbiconId = habbiconId,
                    SecondsSinceSent = secondsSinceSent,
                    MessageId = messageId.ToString(),
                    ConfirmationId = 0,
                    SenderId = senderId.Value,
                    SenderName = senderName,
                    SenderFigure = senderFigure,
                }
            )
        );

        _pendingDeliveredIds.Add(messageId);

        return Task.CompletedTask;
    }

    public async Task<List<MessageHistoryEntrySnapshot>> GetMessageHistoryAsync(
        PlayerId friendId,
        CancellationToken ct
    )
    {
        int messageHistoryLimit = await this
            .GrainFactory.GetServerConfigGrain()
            .GetIntAsync(
                MessengerConfig.MessageHistoryLimitKey,
                MessengerConfig.MessageHistoryLimitDefault
            )
            .ConfigureAwait(true);

        await using VortexDbContext dbCtx = await dbCtxFactory.CreateDbContextAsync(ct);

        int selfIdInt = SelfId;
        int friendIdInt = friendId.Value;
        DateTime now = DateTime.UtcNow;

        List<MessengerMessageEntity> messages = await dbCtx
            .MessengerMessages.AsNoTracking()
            .Include(m => m.SenderEntity)
            .Where(m =>
                m.DeletedAt == null
                && (
                    (m.SenderEntityId == selfIdInt && m.ReceiverEntityId == friendIdInt)
                    || (m.SenderEntityId == friendIdInt && m.ReceiverEntityId == selfIdInt)
                )
            )
            .OrderByDescending(m => m.Timestamp)
            .Take(messageHistoryLimit)
            .ToListAsync(ct);

        return messages
            .Select(m => new MessageHistoryEntrySnapshot
            {
                SenderId = m.SenderEntityId,
                SenderName = m.SenderEntity.Name,
                SenderFigure = m.SenderEntity.Figure,
                Message = m.Message,
                HabbiconId = m.HabbiconId,
                SecondsSinceSent = (int)(now - m.Timestamp).TotalSeconds,
                MessageId = m.Id.ToString(),
            })
            .ToList();
    }
}
