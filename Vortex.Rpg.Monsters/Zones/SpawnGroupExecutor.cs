using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.World.Rules;
using Vortex.Rpg.Zones.Rules;

namespace Vortex.Rpg.Monsters.Zones;

/// <summary>
/// SpawnGroup {spawn} (spec Z1 §6.1): wakes the zone's dormant spawns of that id or group, each
/// bringing one wave at the next tick a player is there. The zone's simulation is this module's own;
/// another kind of simulation has no spawns to wake.
/// </summary>
public sealed class SpawnGroupExecutor : ZoneActionExecutor<SpawnGroupAction>
{
    protected override Task RunAsync(
        SpawnGroupAction action,
        ZoneRuleContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.Simulation is RpgZoneSimulation simulation)
        {
            simulation.Spawner.WakeGroup(action.Spawn);
        }

        return Task.CompletedTask;
    }
}
