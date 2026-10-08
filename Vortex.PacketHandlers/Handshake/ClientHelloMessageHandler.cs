using System.Threading;
using System.Threading.Tasks;
using Vortex.Messages.Registry;
using Vortex.Primitives.Networking.Revisions;
using Vortex.Protocol.Messages.Incoming.Handshake;
using Vortex.Protocol.Messages.Outgoing.Handshake;

namespace Vortex.PacketHandlers.Handshake;

public class ClientHelloMessageHandler(IRevisionManager revisions)
    : IMessageHandler<ClientHelloMessage>
{
    private readonly IRevisionManager _revisions = revisions;

    public async ValueTask HandleAsync(
        ClientHelloMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        // The client announces its own build here, and a revision declares the same string as its
        // id (Revision20260701.Revision is "AMBREIA-DEV"). A build nobody
        // registered would leave the session on an id no revision answers to: every packet would
        // then fail to parse until the failure budget closed it. Closed now instead.
        if (message.Production is null || _revisions.GetRevision(message.Production) is null)
        {
            // DisconnectReason keeps id 4000 in every revision, so even a client on a table this
            // server no longer speaks can read why it is dropped and offer to reload.
            await ctx.SendComposerAsync(
                    new DisconnectReasonEventMessageComposer
                    {
                        Reason = DisconnectReasonEventMessageComposer.IncompatibleClientVersion,
                    },
                    ct
                )
                .ConfigureAwait(false);
            await ctx.CloseSessionAsync().ConfigureAwait(false);

            return;
        }

        ctx.SetRevisionId(message.Production);
    }
}
