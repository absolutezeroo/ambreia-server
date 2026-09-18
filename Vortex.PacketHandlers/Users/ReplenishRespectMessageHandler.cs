using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Orleans;
using Vortex.Protocol.Messages.Incoming.Users;

namespace Vortex.PacketHandlers.Users;

/// <summary>
/// Spends one of the day's respect refills. Nothing is sent back: the client has already put its
/// own counters where it expects them to be, and the server's copy is what the next login reports.
/// </summary>
public class ReplenishRespectMessageHandler(IGrainFactory grainFactory)
    : IMessageHandler<ReplenishRespectMessage>
{
    public async ValueTask HandleAsync(
        ReplenishRespectMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0)
        {
            return;
        }

        await grainFactory
            .GetPlayerGrain(ctx.PlayerId)
            .ReplenishRespectAsync(ct)
            .ConfigureAwait(false);
    }
}
