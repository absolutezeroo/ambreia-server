using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.World.Rules;

namespace Vortex.Rooms.Grains.Zones.Triggers.Actions;

/// <summary>Brings a deactivated area back: entered, left and triggered again from the next step.</summary>
public sealed class ActivateZoneExecutor : ZoneActionExecutor<ActivateZoneAction>
{
    protected override Task RunAsync(
        ActivateZoneAction action,
        ZoneTriggerContext ctx,
        CancellationToken ct
    )
    {
        ctx.Zone.SetAreaActive(action.Zone, true);

        return Task.CompletedTask;
    }
}
