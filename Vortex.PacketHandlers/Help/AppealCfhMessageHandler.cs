using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Messages.Registry;
using Vortex.Primitives.Moderation;
using Vortex.Protocol.Messages.Incoming.Help;
using Vortex.Protocol.Messages.Outgoing.Callforhelp;

namespace Vortex.PacketHandlers.Help;

/// <summary>
/// "I disagree" on a report that was closed without a sanction, from the "my reports" window.
/// </summary>
/// <remarks>
/// The client disables its own button and sends nothing further, so the refreshed list pushed back
/// here is the only thing that moves the row from "decided" to "appealed". It is pushed whether the
/// appeal was accepted or not: a refused one means the row was already out of date on their screen,
/// which is exactly the case the refresh fixes.
/// </remarks>
public class AppealCfhMessageHandler(ICfhTicketService tickets) : IMessageHandler<AppealCfhMessage>
{
    public async ValueTask HandleAsync(
        AppealCfhMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0)
        {
            return;
        }

        await tickets.AppealTicketAsync(message.IssueId, ctx.PlayerId, ct).ConfigureAwait(false);

        ImmutableArray<CfhReportStatusSnapshot> reports = await tickets
            .GetReportHistoryForReporterAsync(ctx.PlayerId, ct)
            .ConfigureAwait(false);

        await ctx.SendComposerAsync(new MyCfhReportStatusMessageComposer { Reports = reports }, ct)
            .ConfigureAwait(false);
    }
}
