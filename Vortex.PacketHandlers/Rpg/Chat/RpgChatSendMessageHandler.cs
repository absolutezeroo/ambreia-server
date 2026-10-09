using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Orleans;
using Vortex.Protocol.Messages.Incoming.Rpg.Chat;

namespace Vortex.PacketHandlers.Rpg.Chat;

/// <summary>A chat message (spec F3c §4): the player's presence routes or refuses it.</summary>
public class RpgChatSendMessageHandler(IGrainFactory grainFactory)
    : IMessageHandler<RpgChatSendMessage>
{
    public async ValueTask HandleAsync(
        RpgChatSendMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0)
        {
            return;
        }

        await grainFactory
            .GetPlayerPresenceGrain(ctx.PlayerId)
            .SendChatAsync(message.Channel, message.Target, message.Text, ct)
            .ConfigureAwait(false);
    }
}
