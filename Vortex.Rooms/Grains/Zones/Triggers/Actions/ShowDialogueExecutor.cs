using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Players;
using Vortex.Primitives.World.Rules;

namespace Vortex.Rooms.Grains.Zones.Triggers.Actions;

/// <summary>A bubble for the player who set it off, or for everyone in the zone on a clock.</summary>
public sealed class ShowDialogueExecutor : ZoneActionExecutor<ShowDialogueAction>
{
    protected override Task RunAsync(
        ShowDialogueAction action,
        ZoneTriggerContext ctx,
        CancellationToken ct
    )
    {
        PlayerId[] to = ctx.PlayerId.Value > 0 ? [ctx.PlayerId] : [.. ctx.Zone.Visitors.Keys];

        foreach (PlayerId player in to)
        {
            ZoneBubble.Show(ctx.Room, player, "zone.dialogue", action.Dialogue);
        }

        return Task.CompletedTask;
    }
}
