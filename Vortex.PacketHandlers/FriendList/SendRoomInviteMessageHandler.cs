using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;
using Vortex.Logging.Extensions;
using Vortex.Messages.Registry;
using Vortex.Primitives.FriendList.Grains;
using Vortex.Primitives.Moderation.WordFilter;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Protocol.Messages.Incoming.FriendList;
using Vortex.Protocol.Messages.Outgoing.Notifications;

namespace Vortex.PacketHandlers.FriendList;

public class SendRoomInviteMessageHandler(
    IGrainFactory grainFactory,
    IWordFilter wordFilter,
    ILogger<SendRoomInviteMessageHandler> logger
) : IMessageHandler<SendRoomInviteMessage>
{
    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly IWordFilter _wordFilter = wordFilter;
    private readonly ILogger<SendRoomInviteMessageHandler> _logger = logger;

    public async ValueTask HandleAsync(
        SendRoomInviteMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0 || message.FriendIds.Count == 0)
        {
            return;
        }

        // Once, before the fan-out: one invite to twenty friends is one thing the player wrote, and
        // one line in the journal.
        WordFilterResult filtered = _wordFilter.Apply(
            message.Message,
            WordFilterSource.RoomInvite,
            ctx.PlayerId
        );

        if (filtered.Refused)
        {
            await ctx.SendComposerAsync(WordFilterNotice.Create(_wordFilter.RefusedMessage), ct)
                .ConfigureAwait(false);

            return;
        }

        // Fire-and-forget room invites to each friend's grain, in parallel
        List<Task> pending = new(message.FriendIds.Count);

        foreach (int friendId in message.FriendIds)
        {
            IMessengerGrain friendGrain = _grainFactory.GetMessengerGrain(PlayerId.Parse(friendId));
            pending.Add(
                friendGrain.ReceiveRoomInviteAsync(
                    ctx.PlayerId,
                    filtered.Text,
                    CancellationToken.None
                )
            );
        }

        Task.WhenAll(pending)
            .LogAndForget(
                _logger,
                "Failed to deliver one or more room invites from player {PlayerId}",
                ctx.PlayerId
            );

        await ValueTask.CompletedTask.ConfigureAwait(false);
    }
}
