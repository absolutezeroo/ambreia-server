using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Camera;
using Vortex.Primitives.Orleans;
using Vortex.Protocol.Messages.Incoming.Camera;
using Vortex.Protocol.Messages.Outgoing.Camera;

namespace Vortex.PacketHandlers.Camera;

/// <summary>
/// The shot. The client renders the photo locally and sends the scene; the server keeps it and
/// answers with the path the photo will be served from.
/// </summary>
/// <remarks>
/// Orchestration only, deliberately. The payload is one zlib blob built by the client, and every
/// decision about whether to trust it — the ceilings, the freshness window, the url allowlist, the
/// rate limit — belongs with the state that rate-limits it rather than with the packet that carried
/// it. An answer always goes out: an empty url is the client's only way of hearing "no", and silence
/// leaves its widget waiting forever.
/// </remarks>
public class RenderRoomMessageHandler(IGrainFactory grainFactory)
    : IMessageHandler<RenderRoomMessage>
{
    public async ValueTask HandleAsync(
        RenderRoomMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0)
        {
            return;
        }

        CameraRenderOutcome outcome = await grainFactory
            .GetPlayerCameraGrain(ctx.PlayerId)
            .RenderAsync(ctx.RoomId, message.Payload, ct)
            .ConfigureAwait(false);

        await ctx.SendComposerAsync(new CameraStorageUrlMessageComposer { Url = outcome.Url }, ct)
            .ConfigureAwait(false);
    }
}
