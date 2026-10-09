using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Orleans;
using Vortex.Protocol.Messages.Incoming.Rpg.Items;

namespace Vortex.PacketHandlers.Rpg.Items;

/// <summary>Wearing an item (spec Objets §17); the character checks class, level and slot.</summary>
public class RpgItemEquipMessageHandler(IGrainFactory grainFactory)
    : IMessageHandler<RpgItemEquipMessage>
{
    public async ValueTask HandleAsync(
        RpgItemEquipMessage message,
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
            .EquipItemAsync(message.BagSlot)
            .ConfigureAwait(false);
    }
}
