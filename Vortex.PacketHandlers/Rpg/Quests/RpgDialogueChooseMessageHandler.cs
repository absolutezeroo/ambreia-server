using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Orleans;
using Vortex.Protocol.Messages.Incoming.Rpg.Quests;

namespace Vortex.PacketHandlers.Rpg.Quests;

/// <summary>A dialogue choice (spec M2a §3.1); his room answers.</summary>
public class RpgDialogueChooseMessageHandler(IGrainFactory grainFactory)
    : IMessageHandler<RpgDialogueChooseMessage>
{
    public async ValueTask HandleAsync(
        RpgDialogueChooseMessage message,
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
            .ChooseDialogueAsync(ctx.AsActionContext(), message.Choice, ct)
            .ConfigureAwait(false);
    }
}
