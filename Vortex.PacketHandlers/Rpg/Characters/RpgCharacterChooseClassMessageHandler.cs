using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Orleans;
using Vortex.Protocol.Messages.Incoming.Rpg.Characters;

namespace Vortex.PacketHandlers.Rpg.Characters;

/// <summary>A real class in place of « Sans classe » (spec F3a §5.3); the grain checks ownership
/// against the session's account and answers.</summary>
public class RpgCharacterChooseClassMessageHandler(IGrainFactory grainFactory)
    : IMessageHandler<RpgCharacterChooseClassMessage>
{
    public async ValueTask HandleAsync(
        RpgCharacterChooseClassMessage message,
        MessageContext ctx,
        CancellationToken ct
    ) =>
        await grainFactory
            .GetAccountCharactersGrain(ctx.AccountId)
            .ChooseClassAsync(message.PlayerId, message.ClassId)
            .ConfigureAwait(false);
}
