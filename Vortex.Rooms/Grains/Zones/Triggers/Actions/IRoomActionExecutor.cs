using System;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.World.Rules;

namespace Vortex.Rooms.Grains.Zones.Triggers.Actions;

/// <summary>
/// Carries out one kind of action the room itself owns (plan D13): it acts on the room. A module's
/// action is a <c>Vortex.Rpg.Zones.Rules.IZoneActionExecutor</c> instead (spec Z1 §4.2).
/// </summary>
public interface IRoomActionExecutor
{
    public Type ActionType { get; }

    public Task RunAsync(ZoneAction action, ZoneTriggerContext ctx, CancellationToken ct);
}
