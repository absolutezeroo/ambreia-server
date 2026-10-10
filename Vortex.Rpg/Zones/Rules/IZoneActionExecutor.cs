using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.World.Rules;

namespace Vortex.Rpg.Zones.Rules;

/// <summary>
/// Carries out one kind of zone action, declared by the module that owns what it does
/// (<see cref="ZoneRuleServices.AddZoneAction{T}"/>). It runs inside the room's turn.
/// </summary>
public interface IZoneActionExecutor : IZoneRuleDeclaration
{
    public Task RunAsync(ZoneAction action, ZoneRuleContext ctx, CancellationToken ct);
}
