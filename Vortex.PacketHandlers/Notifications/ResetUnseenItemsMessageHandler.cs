using System.Threading;
using System.Threading.Tasks;
using Vortex.Messages.Registry;
using Vortex.Protocol.Messages.Incoming.Notifications;

namespace Vortex.PacketHandlers.Notifications;

/// <summary>
/// Nothing to clear. The unseen mark is pushed once, when the item arrives
/// (<c>PlayerInventoryModule.OnFurnitureAddedAsync</c>), and lives in the client's own tracker from
/// then on — we hold no set of our own, so a tab being opened tells us nothing we store.
/// </summary>
/// <remarks>
/// The message is parsed in full even so. It carries the category, and a parser that silently drops
/// it would leave the next person believing the client sends an empty body.
/// </remarks>
public class ResetUnseenItemsMessageHandler : IMessageHandler<ResetUnseenItemsMessage>
{
    public async ValueTask HandleAsync(
        ResetUnseenItemsMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        await ValueTask.CompletedTask.ConfigureAwait(false);
    }
}
