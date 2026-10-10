using System;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.World.Rules;

namespace Vortex.Rpg.Zones.Rules;

public abstract class ZoneActionExecutor<TAction> : IZoneActionExecutor
    where TAction : ZoneAction
{
    public Type RuleType => typeof(TAction);

    public Task RunAsync(ZoneAction action, ZoneRuleContext ctx, CancellationToken ct) =>
        RunAsync((TAction)action, ctx, ct);

    protected abstract Task RunAsync(TAction action, ZoneRuleContext ctx, CancellationToken ct);
}
