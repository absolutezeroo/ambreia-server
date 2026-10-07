using System;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.World.Rules;

namespace Vortex.Rooms.Grains.Zones.Triggers.Actions;

/// <summary>Carries out one kind of action (plan D13).</summary>
public interface IZoneActionExecutor
{
    public Type ActionType { get; }

    public Task RunAsync(ZoneAction action, ZoneTriggerContext ctx, CancellationToken ct);
}
