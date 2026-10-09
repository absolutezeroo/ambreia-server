using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Orleans;
using Vortex.Protocol.Messages.Incoming.Rpg.Items;

namespace Vortex.PacketHandlers.Rpg.Items;

/// <summary>Taking an item off (spec Objets §17); the character checks and answers.</summary>
public class RpgItemUnequipMessageHandler(IGrainFactory grainFactory)
    : IMessageHandler<RpgItemUnequipMessage>
{
    public async ValueTask HandleAsync(
        RpgItemUnequipMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0)
        {
            return;
        }

        await grainFactory
            .GetPlayerCharacterGrain(ctx.PlayerId)
            .UnequipItemAsync(message.EquipmentSlot)
            .ConfigureAwait(false);
    }
}
