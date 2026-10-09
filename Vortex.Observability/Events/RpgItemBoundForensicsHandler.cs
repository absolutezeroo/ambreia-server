using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Events.Rpg;
using Vortex.Primitives.Observability;

namespace Vortex.Observability.Events;

/// <summary>A binding on equip (spec Objets §13): the copy is the player's for good.</summary>
public sealed class RpgItemBoundForensicsHandler(IItemForensics forensics)
    : IEventHandler<ItemBoundEvent>
{
    public ValueTask HandleAsync(ItemBoundEvent e, EventContext ctx, CancellationToken ct)
    {
        forensics.Record(
            new ItemForensicEvent
            {
                ItemId = e.ItemId,
                EventType = ItemEventType.RpgBound,
                ActorPlayerId = e.PlayerId.Value,
                ToOwnerId = e.PlayerId.Value,
                Data = JsonSerializer.Serialize(new { definition = e.DefinitionId }),
            }
        );

        return ValueTask.CompletedTask;
    }
}
