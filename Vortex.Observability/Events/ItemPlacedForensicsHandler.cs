using System.Threading;
using System.Threading.Tasks;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Observability;

namespace Vortex.Observability.Events;

public sealed class ItemPlacedForensicsHandler(IItemForensics forensics)
    : IEventHandler<ItemPlacedEvent>
{
    public ValueTask HandleAsync(ItemPlacedEvent e, EventContext ctx, CancellationToken ct)
    {
        forensics.Record(
            new ItemForensicEvent
            {
                ItemId = e.ItemId,
                EventType = ItemEventType.Placed,
                ActorPlayerId = e.ActorPlayerId,
                ToOwnerId = e.OwnerId,
                RoomId = e.RoomId,
                Data = e.Data,
            }
        );

        return ValueTask.CompletedTask;
    }
}
