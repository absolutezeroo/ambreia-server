using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Orleans;
using Vortex.Protocol.Messages.Incoming.Rpg.Combat;

namespace Vortex.PacketHandlers.Rpg.Combat;

/// <summary>Le joueur KO choisit (M1d §2.3) ; sa salle transmet.</summary>
public class RpgDeathChoiceMessageHandler(IGrainFactory grainFactory)
    : IMessageHandler<RpgDeathChoiceMessage>
{
    public async ValueTask HandleAsync(
        RpgDeathChoiceMessage message,
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
            .DeathChoiceAsync(ctx.AsActionContext(), message.Choice, ct)
            .ConfigureAwait(false);
    }
}
