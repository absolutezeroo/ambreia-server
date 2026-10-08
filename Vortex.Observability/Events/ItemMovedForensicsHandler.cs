using System.Threading;
using System.Threading.Tasks;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Observability;

namespace Vortex.Observability.Events;

public sealed class ItemMovedForensicsHandler(IItemForensics forensics)
    : IEventHandler<ItemMovedEvent>
{
    public ValueTask HandleAsync(ItemMovedEvent e, EventContext ctx, CancellationToken ct)
    {
        forensics.Record(
            new ItemForensicEvent
            {
                ItemId = e.ItemId,
                EventType = ItemEventType.Moved,
                ActorPlayerId = e.ActorPlayerId,
                RoomId = e.RoomId,
                Data = e.Data,
            }
        );

        return ValueTask.CompletedTask;
    }
}
