using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Orleans;
using Vortex.Protocol.Messages.Incoming.Rpg.Quests;

namespace Vortex.PacketHandlers.Rpg.Quests;

/// <summary>The player talks to an NPC (spec M2a §3.1); his room answers.</summary>
public class RpgNpcTalkMessageHandler(IGrainFactory grainFactory)
    : IMessageHandler<RpgNpcTalkMessage>
{
    public async ValueTask HandleAsync(
        RpgNpcTalkMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0 || ctx.RoomId <= 0)
        {
            return;
        }

        await grainFactory
            .GetRoomNpcs(ctx.RoomId)
            .TalkToNpcAsync(ctx.AsActionContext(), message.ObjectId, ct)
            .ConfigureAwait(false);
    }
}
