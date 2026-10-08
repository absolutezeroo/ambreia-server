using System.Threading;
using System.Threading.Tasks;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Observability;

namespace Vortex.Observability.Events;

public sealed class ItemPickedUpForensicsHandler(IItemForensics forensics)
    : IEventHandler<ItemPickedUpEvent>
{
    public ValueTask HandleAsync(ItemPickedUpEvent e, EventContext ctx, CancellationToken ct)
    {
        forensics.Record(
            new ItemForensicEvent
            {
                ItemId = e.ItemId,
                EventType = ItemEventType.PickedUp,
                ActorPlayerId = e.ActorPlayerId,
                FromOwnerId = e.FromOwnerId,
                ToOwnerId = e.ToOwnerId,
                RoomId = e.RoomId,
            }
        );

        return ValueTask.CompletedTask;
    }
}
