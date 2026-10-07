using System.Threading;
using System.Threading.Tasks;

namespace Vortex.Rooms.Grains.Zones.Interactions;

/// <summary>What using a zone furni does, for one value of its <c>interaction</c> (plan D10).</summary>
public interface IZoneInteraction
{
    public string Kind { get; }

    public Task UseAsync(ZoneUse use, CancellationToken ct);
}
