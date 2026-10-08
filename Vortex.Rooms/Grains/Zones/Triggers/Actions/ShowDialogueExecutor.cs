using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.World.Rules;
using Vortex.Rooms.Grains.Zones.Runtime;

namespace Vortex.Rooms.Grains.Zones.Triggers.Actions;

/// <summary>
/// A bubble for the player who set it off, or -- on a clock, set off by nobody -- one message on
/// the room's stream for everyone in it.
/// </summary>
public sealed class ShowDialogueExecutor : ZoneActionExecutor<ShowDialogueAction>
{
    protected override Task RunAsync(
        ShowDialogueAction action,
        ZoneTriggerContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId.Value > 0)
        {
            ZoneBubble.Show(ctx.Room, ctx.PlayerId, "zone.dialogue", action.Dialogue);
        }
        else
        {
            ZoneBubble.ShowToRoom(ctx.Room, "zone.dialogue", action.Dialogue);
        }

        return Task.CompletedTask;
    }
}
