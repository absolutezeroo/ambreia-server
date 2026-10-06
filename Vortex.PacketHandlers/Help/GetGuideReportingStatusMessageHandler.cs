using System.Threading;
using System.Threading.Tasks;
using Vortex.Messages.Registry;
using Vortex.Protocol.Messages.Incoming.Help;
using Vortex.Protocol.Messages.Outgoing.Help;

namespace Vortex.PacketHandlers.Help;

/// <summary>
/// "Do I already have something open with a guide?", asked before the help window opens.
/// </summary>
/// <remarks>
/// The reply is what opens the window: status 0 tells the client to show the new-help flow, so
/// answering nothing left the Help button doing nothing at all. Guides were removed (M0 bis), so
/// nobody ever has a session pending and the answer is always 0.
/// </remarks>
public class GetGuideReportingStatusMessageHandler : IMessageHandler<GetGuideReportingStatusMessage>
{
    /// <summary>Nothing pending: open the new-help window.</summary>
    private const int StatusNothingPending = 0;

    public async ValueTask HandleAsync(
        GetGuideReportingStatusMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0)
        {
            return;
        }

        await ctx.SendComposerAsync(
                new GuideReportingStatusMessageComposer { StatusCode = StatusNothingPending },
                ct
            )
            .ConfigureAwait(false);
    }
}
