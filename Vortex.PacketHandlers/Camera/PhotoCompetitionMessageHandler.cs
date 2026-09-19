using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Camera;
using Vortex.Primitives.Orleans;
using Vortex.Protocol.Messages.Incoming.Camera;
using Vortex.Protocol.Messages.Outgoing.Camera;

namespace Vortex.PacketHandlers.Camera;

/// <summary>"Enter this photo in the competition." No arguments.</summary>
public class PhotoCompetitionMessageHandler(IGrainFactory grainFactory)
    : IMessageHandler<PhotoCompetitionMessage>
{
    public async ValueTask HandleAsync(
        PhotoCompetitionMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0)
        {
            return;
        }

        CameraCompetitionOutcome outcome = await grainFactory
            .GetPlayerCameraGrain(ctx.PlayerId)
            .SubmitHeldPhotoToCompetitionAsync(ct)
            .ConfigureAwait(false);

        await ctx.SendComposerAsync(
                new CompetitionStatusMessageComposer
                {
                    Ok = outcome.Ok,
                    ErrorReason = outcome.ErrorReason,
                },
                ct
            )
            .ConfigureAwait(false);
    }
}
