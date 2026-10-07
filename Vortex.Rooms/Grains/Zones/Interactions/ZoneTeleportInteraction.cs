using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.World.Layers;

namespace Vortex.Rooms.Grains.Zones.Interactions;

/// <summary>
/// Sends the player where the tag says: a passage id takes that passage (any zone), an entry id
/// moves them to that entry of this zone.
/// </summary>
public sealed class ZoneTeleportInteraction : IZoneInteraction
{
    public string Kind => "teleport";

    public async Task UseAsync(ZoneUse use, CancellationToken ct)
    {
        CompiledZone zone = use.Room._state.ZoneRuntime!.Compiled;

        if (use.Player is null || string.IsNullOrEmpty(use.Decor.Tag))
        {
            return;
        }

        if (zone.Document.Passages.FirstOrDefault(p => p.Id == use.Decor.Tag) is { } passage)
        {
            await use.Room.ZonePassages.TakeAsync(use.Player, passage, ct);
        }
        else if (zone.Entries.TryGetValue(use.Decor.Tag, out ZoneEntry? entry))
        {
            await use.Room.ZonePassages.MoveToAsync(use.Player, entry, ct);
        }
    }
}
