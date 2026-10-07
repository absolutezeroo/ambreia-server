using System;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.World.Rules;

namespace Vortex.Rooms.Grains.Zones.Triggers.Actions;

public abstract class ZoneActionExecutor<TAction> : IZoneActionExecutor
    where TAction : ZoneAction
{
    public Type ActionType => typeof(TAction);

    public Task RunAsync(ZoneAction action, ZoneTriggerContext ctx, CancellationToken ct) =>
        RunAsync((TAction)action, ctx, ct);

    protected abstract Task RunAsync(TAction action, ZoneTriggerContext ctx, CancellationToken ct);
}
