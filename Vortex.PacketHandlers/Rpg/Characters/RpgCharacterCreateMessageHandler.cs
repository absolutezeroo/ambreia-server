using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Orleans;
using Vortex.Protocol.Messages.Incoming.Rpg.Characters;

namespace Vortex.PacketHandlers.Rpg.Characters;

/// <summary>A new character for the session's account (spec F3a §5.3); the grain answers.</summary>
public class RpgCharacterCreateMessageHandler(IGrainFactory grainFactory)
    : IMessageHandler<RpgCharacterCreateMessage>
{
    public async ValueTask HandleAsync(
        RpgCharacterCreateMessage message,
        MessageContext ctx,
        CancellationToken ct
    ) =>
        await grainFactory
            .GetAccountCharactersGrain(ctx.AccountId)
            .CreateAsync(message.Name, message.Figure, message.Gender)
            .ConfigureAwait(false);
}
