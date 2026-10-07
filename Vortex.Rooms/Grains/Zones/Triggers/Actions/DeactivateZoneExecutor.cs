using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.World.Rules;

namespace Vortex.Rooms.Grains.Zones.Triggers.Actions;

/// <summary>
/// Switches an area off: it no longer rules its cells, no one enters it, and those inside leave it
/// at their next step (one last LeaveZone); its triggers stay silent until it is switched on.
/// </summary>
public sealed class DeactivateZoneExecutor : ZoneActionExecutor<DeactivateZoneAction>
{
    protected override Task RunAsync(
        DeactivateZoneAction action,
        ZoneTriggerContext ctx,
        CancellationToken ct
    )
    {
        ctx.Zone.SetAreaActive(action.Zone, false);

        return Task.CompletedTask;
    }
}
