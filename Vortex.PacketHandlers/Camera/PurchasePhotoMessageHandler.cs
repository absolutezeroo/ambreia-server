using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Orleans;
using Vortex.Protocol.Messages.Incoming.Camera;
using Vortex.Protocol.Messages.Outgoing.Camera;

namespace Vortex.PacketHandlers.Camera;

/// <summary>
/// "Buy this photo." No arguments — the server is holding the shot they mean.
/// </summary>
/// <remarks>
/// Nothing is sent when the purchase does not happen. The client has one reply for this message and
/// it is a success with no body; there is no error twin to send, and inventing one would be a packet
/// no client reads.
/// </remarks>
public class PurchasePhotoMessageHandler(IGrainFactory grainFactory)
    : IMessageHandler<PurchasePhotoMessage>
{
    public async ValueTask HandleAsync(
        PurchasePhotoMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0)
        {
            return;
        }

        bool bought = await grainFactory
            .GetPlayerCameraGrain(ctx.PlayerId)
            .PurchaseHeldPhotoAsync(ct)
            .ConfigureAwait(false);

        if (bought)
        {
            await ctx.SendComposerAsync(new CameraPurchaseOKMessageComposer(), ct)
                .ConfigureAwait(false);
        }
    }
}
