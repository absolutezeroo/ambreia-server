using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Orleans.Snapshots.Players;
using Vortex.Primitives.Players.Grains;
using Vortex.Protocol.Messages.Incoming.Handshake;
using Vortex.Protocol.Messages.Outgoing.Handshake;

namespace Vortex.PacketHandlers.Handshake;

public class InfoRetrieveMessageHandler(IGrainFactory grainFactory)
    : IMessageHandler<InfoRetrieveMessage>
{
    private readonly IGrainFactory _grainFactory = grainFactory;

    public async ValueTask HandleAsync(
        InfoRetrieveMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        // The client only asks for this after a successful SSO, but nothing on the wire enforces
        // that order: arriving first, it would activate the player grain on the unbound session's
        // -1 and throw "the specified player could not be found" out of the pipeline.
        if (ctx.PlayerId <= 0)
        {
            return;
        }

        IPlayerGrain player = _grainFactory.GetPlayerGrain(ctx.PlayerId);
        PlayerSummarySnapshot snapshot = await player.GetSummaryAsync(ct).ConfigureAwait(false);

        await ctx.SendComposerAsync(new UserObjectMessage { Player = snapshot }, ct)
            .ConfigureAwait(false);
    }
}
