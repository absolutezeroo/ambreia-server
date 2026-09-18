using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Camera;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Server;
using Vortex.Protocol.Messages.Incoming.Camera;
using Vortex.Protocol.Messages.Outgoing.Camera;

namespace Vortex.PacketHandlers.Camera;

/// <summary>
/// What the camera charges. Asked once per room entry, and answered by nothing until now.
/// </summary>
/// <remarks>
/// <c>CameraWidgetHandler.onInitCameraEvent</c> assigns the three prices and nothing else sets them,
/// so an unanswered request left the widget on its own defaults for the whole visit. The prices are
/// admin-editable config rather than table content, which is what <c>IServerConfigGrain</c> is for:
/// one round trip for the group, each key falling back to the compiled default.
/// </remarks>
public class RequestCameraConfigurationMessageHandler(IGrainFactory grainFactory)
    : IMessageHandler<RequestCameraConfigurationMessage>
{
    private readonly IGrainFactory _grainFactory = grainFactory;

    public async ValueTask HandleAsync(
        RequestCameraConfigurationMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0)
        {
            return;
        }

        ImmutableDictionary<string, string> values = await _grainFactory
            .GetServerConfigGrain()
            .GetManyAsync(CameraConfig.AllKeys)
            .ConfigureAwait(false);

        await ctx.SendComposerAsync(
                new InitCameraMessageComposer
                {
                    CreditPrice = ServerConfigValues.GetInt(
                        values,
                        CameraConfig.CreditPriceKey,
                        CameraConfig.DefaultCreditPrice
                    ),
                    DucketPrice = ServerConfigValues.GetInt(
                        values,
                        CameraConfig.DucketPriceKey,
                        CameraConfig.DefaultDucketPrice
                    ),
                    PublishDucketPrice = ServerConfigValues.GetInt(
                        values,
                        CameraConfig.PublishDucketPriceKey,
                        CameraConfig.DefaultPublishDucketPrice
                    ),
                },
                ct
            )
            .ConfigureAwait(false);
    }
}
