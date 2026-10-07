using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.World.Rules;

namespace Vortex.Rooms.Grains.Zones.Triggers.Actions;

/// <summary>Silences an area: nobody enters or leaves it, so none of its triggers fire.</summary>
public sealed class DeactivateZoneExecutor : ZoneActionExecutor<DeactivateZoneAction>
{
    protected override Task RunAsync(
        DeactivateZoneAction action,
        ZoneTriggerContext ctx,
        CancellationToken ct
    )
    {
        ctx.Zone.InactiveAreas.Add(action.Zone);

        return Task.CompletedTask;
    }
}
