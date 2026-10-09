using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Action;

namespace Vortex.Rooms.Grains;

/// <summary>Targets (spec F3b §6): <c>RoomTargetModule</c> does the work.</summary>
public sealed partial class RoomGrain
{
    public Task SelectTargetAsync(ActionContext ctx, int objectId, CancellationToken ct) =>
        ctx.PlayerId <= 0 ? Task.CompletedTask : TargetModule.SelectAsync(ctx, objectId, ct);
}
