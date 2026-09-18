using System.Threading;
using System.Threading.Tasks;
using Vortex.Messages.Registry;
using Vortex.Protocol.Messages.Incoming.Notifications;

namespace Vortex.PacketHandlers.Notifications;

/// <summary>
/// The narrower half of <see cref="ResetUnseenItemsMessageHandler"/> — some of a tab's items rather
/// than all of them — and a no-op for the same reason: the unseen mark is the client's to keep.
/// </summary>
public class ResetUnseenItemIdsMessageHandler : IMessageHandler<ResetUnseenItemIdsMessage>
{
    public async ValueTask HandleAsync(
        ResetUnseenItemIdsMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        await ValueTask.CompletedTask.ConfigureAwait(false);
    }
}
