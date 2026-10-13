using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Orleans;
using Vortex.Protocol.Messages.Incoming.Rpg.Combat;

namespace Vortex.PacketHandlers.Rpg.Combat;

/// <summary>Le joueur démarre ou arrête son attaque auto (M1d §2.2) ; sa salle transmet.</summary>
public class RpgAutoAttackMessageHandler(IGrainFactory grainFactory)
    : IMessageHandler<RpgAutoAttackMessage>
{
    public async ValueTask HandleAsync(
        RpgAutoAttackMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0 || ctx.RoomId <= 0)
        {
            return;
        }

        await grainFactory
            .GetRoomCombat(ctx.RoomId)
            .SetAutoAttackAsync(ctx.AsActionContext(), message.Active, ct)
            .ConfigureAwait(false);
    }
}
