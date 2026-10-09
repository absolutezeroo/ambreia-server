using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Events.Rpg;
using Vortex.Primitives.Observability;

namespace Vortex.Observability.Events;

/// <summary>A destroy (spec Objets §13): the player's own gesture on a copy they held.</summary>
public sealed class RpgItemDestroyedForensicsHandler(IItemForensics forensics)
    : IEventHandler<ItemDestroyedEvent>
{
    public ValueTask HandleAsync(ItemDestroyedEvent e, EventContext ctx, CancellationToken ct)
    {
        forensics.Record(
            new ItemForensicEvent
            {
                ItemId = e.ItemId,
                EventType = ItemEventType.RpgDestroyed,
                ActorPlayerId = e.PlayerId.Value,
                FromOwnerId = e.PlayerId.Value,
                Data = JsonSerializer.Serialize(
                    new { definition = e.DefinitionId, quantity = e.Quantity }
                ),
            }
        );

        return ValueTask.CompletedTask;
    }
}
