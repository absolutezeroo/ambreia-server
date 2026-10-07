using System.Threading;
using System.Threading.Tasks;

namespace Vortex.Rooms.Grains.Zones.Interactions;

/// <summary>Flips between 0 and 1; what it sets off is a trigger on its Interact event.</summary>
public sealed class ZoneLeverInteraction : IZoneInteraction
{
    public string Kind => "lever";

    public Task UseAsync(ZoneUse use, CancellationToken ct) =>
        use.Item.Logic.SetStateAsync(use.Item.Logic.GetState() == 0 ? 1 : 0);
}
