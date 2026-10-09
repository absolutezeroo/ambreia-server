using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Orleans;
using Vortex.Protocol.Messages.Incoming.Rpg.Emote;

namespace Vortex.PacketHandlers.Rpg.Emote;

/// <summary>An RP action: the player's room shows it, or ignores it.</summary>
public class RpgEmoteMessageHandler(IGrainFactory grainFactory) : IMessageHandler<RpgEmoteMessage>
{
    public async ValueTask HandleAsync(
        RpgEmoteMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0 || ctx.RoomId <= 0)
        {
            return;
        }

        await grainFactory
            .GetRoomEmotes(ctx.RoomId)
            .EmoteAsync(ctx.AsActionContext(), message.Key, ct)
            .ConfigureAwait(false);
    }
}
