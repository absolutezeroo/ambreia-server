using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.FriendList.Enums;
using Vortex.Primitives.FriendList.Grains;
using Vortex.Primitives.Moderation.WordFilter;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Protocol.Messages.Incoming.FriendList;
using Vortex.Protocol.Messages.Outgoing.FriendList;
using Vortex.Protocol.Messages.Outgoing.Notifications;

namespace Vortex.PacketHandlers.FriendList;

public class SendMsgMessageHandler(IGrainFactory grainFactory, IWordFilter wordFilter)
    : IMessageHandler<SendMsgMessage>
{
    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly IWordFilter _wordFilter = wordFilter;

    public async ValueTask HandleAsync(
        SendMsgMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0)
        {
            return;
        }

        // Here rather than in MessengerGrain.SendMessageAsync: the filter is about what a player
        // typed, and this handler is where typed text enters.
        WordFilterResult filtered = _wordFilter.Apply(
            message.Message,
            WordFilterSource.Messenger,
            ctx.PlayerId
        );

        if (filtered.Refused)
        {
            // OfflineFailed (10) because the client's error table is a verbatim port of MainView.as's
            // ERROR_MESSAGES and has no "filtered" entry; 10 states the outcome without a cause, as
            // SendMessageAsync already does for the ignore-list refusal. The bubble says why.
            await ctx.SendComposerAsync(WordFilterNotice.Create(_wordFilter.RefusedMessage), ct)
                .ConfigureAwait(false);
            await ctx.SendComposerAsync(
                    new InstantMessageErrorMessageComposer
                    {
                        ErrorCode = InstantMessageErrorCodeType.OfflineFailed,
                        PlayerId = message.ChatId,
                        Message = message.Message,
                    },
                    ct
                )
                .ConfigureAwait(false);

            return;
        }

        IMessengerGrain grain = _grainFactory.GetMessengerGrain(ctx.PlayerId);
        PlayerId receiverId = PlayerId.Parse(message.ChatId);

        InstantMessageErrorCodeType? error = await grain
            .SendMessageAsync(
                receiverId,
                filtered.Text,
                message.ChatId,
                message.ConfirmationId,
                habbiconId: 0,
                ct
            )
            .ConfigureAwait(false);

        if (error.HasValue)
        {
            await ctx.SendComposerAsync(
                    new InstantMessageErrorMessageComposer
                    {
                        ErrorCode = error.Value,
                        PlayerId = message.ChatId,
                        Message = message.Message,
                    },
                    ct
                )
                .ConfigureAwait(false);
        }
    }
}
