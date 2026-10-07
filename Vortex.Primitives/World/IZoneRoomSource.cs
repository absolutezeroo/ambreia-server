using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Rooms;

namespace Vortex.Primitives.World;

/// <summary>What a room asks on activation: is it a zone, and if so, what does it run.</summary>
public interface IZoneRoomSource
{
    /// <summary>The published document of the zone this room runs, or null for an ordinary room.</summary>
    Task<ZoneDocument?> GetPublishedAsync(RoomId roomId, CancellationToken ct);
}
