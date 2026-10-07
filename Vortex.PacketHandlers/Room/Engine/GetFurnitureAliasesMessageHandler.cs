using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Orleans;
using Vortex.Protocol.Messages.Incoming.Room.Engine;
using Vortex.Protocol.Messages.Outgoing.Room.Engine;

namespace Vortex.PacketHandlers.Room.Engine;

public class GetFurnitureAliasesMessageHandler(IGrainFactory grainFactory)
    : IMessageHandler<GetFurnitureAliasesMessage>
{
    private readonly IGrainFactory _grainFactory = grainFactory;

    public async ValueTask HandleAsync(
        GetFurnitureAliasesMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        // The client asks right after AuthenticationOK; before sign-in there is nobody to answer.
        if (ctx.PlayerId <= 0)
        {
            return;
        }

        await _grainFactory
            .GetPlayerPresenceGrain(ctx.PlayerId)
            .SendComposerAsync(new FurnitureAliasesMessageComposer { Aliases = [] })
            .ConfigureAwait(false);
    }
}
