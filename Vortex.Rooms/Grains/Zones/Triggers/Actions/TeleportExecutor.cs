using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.World.Layers;
using Vortex.Primitives.World.Rules;

namespace Vortex.Rooms.Grains.Zones.Triggers.Actions;

/// <summary>Sends the player to an entry: of this zone at once, of another through a passage.</summary>
public sealed class TeleportExecutor : ZoneActionExecutor<TeleportAction>
{
    protected override async Task RunAsync(
        TeleportAction action,
        ZoneTriggerContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.Player is null)
        {
            return;
        }

        string here = ctx.Zone.Compiled.Document.Settings.Id;

        if (string.IsNullOrEmpty(action.Zone) || action.Zone == here)
        {
            if (ctx.Zone.Compiled.Entries.TryGetValue(action.Entry, out ZoneEntry? entry))
            {
                await ctx.Room.ZonePassages.MoveToAsync(ctx.Player, entry, ct);
            }

            return;
        }

        await ctx.Room.ZonePassages.TakeAsync(
            ctx.Player,
            new ZonePassage
            {
                Id = "teleport",
                Area = [],
                DestinationZone = action.Zone,
                DestinationEntry = action.Entry,
            },
            ct
        );
    }
}
