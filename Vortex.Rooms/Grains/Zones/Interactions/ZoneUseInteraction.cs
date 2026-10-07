using System.Threading;
using System.Threading.Tasks;

namespace Vortex.Rooms.Grains.Zones.Interactions;

/// <summary>The furni's own use, open to every player rather than to those with rights.</summary>
public sealed class ZoneUseInteraction : IZoneInteraction
{
    public string Kind => "use";

    public Task UseAsync(ZoneUse use, CancellationToken ct) =>
        use.Item.Logic.OnUseAsync(use.Ctx, use.Param, ct);
}
