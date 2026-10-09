using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Action;

namespace Vortex.Rooms.Grains;

/// <summary>RP actions: <c>RoomEmoteModule</c> does the work.</summary>
public sealed partial class RoomGrain
{
    public Task EmoteAsync(ActionContext ctx, string key, CancellationToken ct) =>
        ctx.PlayerId <= 0 ? Task.CompletedTask : EmoteModule.EmoteAsync(ctx, key);
}
