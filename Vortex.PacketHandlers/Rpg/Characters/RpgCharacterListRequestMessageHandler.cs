using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Orleans;
using Vortex.Protocol.Messages.Incoming.Rpg.Characters;

namespace Vortex.PacketHandlers.Rpg.Characters;

/// <summary>The account's characters again (spec F3a §6.1); the account comes from the session.</summary>
public class RpgCharacterListRequestMessageHandler(IGrainFactory grainFactory)
    : IMessageHandler<RpgCharacterListRequestMessage>
{
    public async ValueTask HandleAsync(
        RpgCharacterListRequestMessage message,
        MessageContext ctx,
        CancellationToken ct
    ) =>
        await grainFactory
            .GetAccountCharactersGrain(ctx.AccountId)
            .ListAsync()
            .ConfigureAwait(false);
}
