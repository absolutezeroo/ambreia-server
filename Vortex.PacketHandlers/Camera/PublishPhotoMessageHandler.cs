using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Camera;
using Vortex.Primitives.Orleans;
using Vortex.Protocol.Messages.Incoming.Camera;
using Vortex.Protocol.Messages.Outgoing.Camera;

namespace Vortex.PacketHandlers.Camera;

/// <summary>"Publish this photo." No arguments, like the other two confirmations.</summary>
public class PublishPhotoMessageHandler(IGrainFactory grainFactory)
    : IMessageHandler<PublishPhotoMessage>
{
    public async ValueTask HandleAsync(
        PublishPhotoMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0)
        {
            return;
        }

        CameraPublishOutcome outcome = await grainFactory
            .GetPlayerCameraGrain(ctx.PlayerId)
            .PublishHeldPhotoAsync(ct)
            .ConfigureAwait(false);

        await ctx.SendComposerAsync(
                new CameraPublishStatusMessageComposer
                {
                    Ok = outcome.Ok,
                    SecondsToWait = outcome.SecondsToWait,
                    ExtraDataId = outcome.ExtraDataId,
                },
                ct
            )
            .ConfigureAwait(false);
    }
}
