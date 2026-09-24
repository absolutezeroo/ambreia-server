using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.PacketHandlers.Moderation;
using Vortex.Primitives.Help;
using Vortex.Primitives.Help.Grains;
using Vortex.Primitives.Moderation.WordFilter;
using Vortex.Primitives.Orleans;
using Vortex.Protocol.Messages.Incoming.Help;

namespace Vortex.PacketHandlers.Help;

/// <summary>
/// A player asking a guide for help. The request is offered to one on-duty guide, or refused
/// outright when nobody is covering that queue.
/// </summary>
public class GuideSessionCreateMessageHandler(IGrainFactory grainFactory, IWordFilter wordFilter)
    : IMessageHandler<GuideSessionCreateMessage>
{
    public async ValueTask HandleAsync(
        GuideSessionCreateMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0)
        {
            return;
        }

        // The description is shown to the guide who picks the request up. None of the client's
        // GuideSessionError codes means "refused for its words", so a refusal is the bubble alone.
        string? description = await wordFilter
            .PassAsync(ctx, message.Description, WordFilterSource.Guide, ct)
            .ConfigureAwait(false);

        if (description is null)
        {
            return;
        }

        GuideRequestOutcome outcome = await grainFactory
            .GetGuideDirectoryGrain()
            .CreateRequestAsync(ctx.PlayerId, message.HelpRequestType, description, ct)
            .ConfigureAwait(false);

        await GuideSessionDispatch.DeliverAsync(grainFactory, outcome, ct).ConfigureAwait(false);
    }
}
