using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.World.Rules;
using Vortex.Rpg.Zones.Rules;

namespace Vortex.Rooms.Tests.Support;

/// <summary>A module's action that only records what it was asked to do, and for whom.</summary>
internal sealed class RecordingAction<TAction> : ZoneActionExecutor<TAction>
    where TAction : ZoneAction
{
    public List<(TAction Action, ZoneRuleContext Ctx)> Runs { get; } = [];

    protected override Task RunAsync(TAction action, ZoneRuleContext ctx, CancellationToken ct)
    {
        Runs.Add((action, ctx));

        return Task.CompletedTask;
    }
}
