using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Events.Rpg;
using Vortex.Primitives.Observability;

namespace Vortex.Observability.Events;

/// <summary>A grant (spec Objets §13): a server gesture, so no actor; the copy goes to the player.</summary>
public sealed class RpgItemGrantedForensicsHandler(IItemForensics forensics)
    : IEventHandler<ItemGrantedEvent>
{
    public ValueTask HandleAsync(ItemGrantedEvent e, EventContext ctx, CancellationToken ct)
    {
        forensics.Record(
            new ItemForensicEvent
            {
                ItemId = e.ItemId,
                EventType = ItemEventType.RpgGranted,
                ToOwnerId = e.PlayerId.Value,
                Data = JsonSerializer.Serialize(
                    new
                    {
                        definition = e.DefinitionId,
                        quantity = e.Quantity,
                        grantKey = e.GrantKey,
                        bound = e.Bound,
                    }
                ),
            }
        );

        return ValueTask.CompletedTask;
    }
}
