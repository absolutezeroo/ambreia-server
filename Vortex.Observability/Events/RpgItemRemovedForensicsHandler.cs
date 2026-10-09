using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Events.Rpg;
using Vortex.Primitives.Observability;

namespace Vortex.Observability.Events;

/// <summary>A removal (spec Objets §13): a server system took from the player's copy, so no actor.</summary>
public sealed class RpgItemRemovedForensicsHandler(IItemForensics forensics)
    : IEventHandler<ItemRemovedEvent>
{
    public ValueTask HandleAsync(ItemRemovedEvent e, EventContext ctx, CancellationToken ct)
    {
        forensics.Record(
            new ItemForensicEvent
            {
                ItemId = e.ItemId,
                EventType = ItemEventType.RpgRemoved,
                FromOwnerId = e.PlayerId.Value,
                Data = JsonSerializer.Serialize(
                    new { definition = e.DefinitionId, quantity = e.Quantity }
                ),
            }
        );

        return ValueTask.CompletedTask;
    }
}
