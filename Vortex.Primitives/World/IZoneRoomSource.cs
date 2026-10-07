using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Rooms;

namespace Vortex.Primitives.World;

/// <summary>What a room asks about zones: is it one, what does it run, where are the others.</summary>
public interface IZoneRoomSource
{
    /// <summary>The published document of the zone this room runs, or null for an ordinary room.</summary>
    Task<ZoneDocument?> GetPublishedAsync(RoomId roomId, CancellationToken ct);

    /// <summary>The room running the zone <paramref name="zoneKey"/>, or null while it is unpublished.</summary>
    Task<RoomId?> FindPublishedRoomAsync(string zoneKey, CancellationToken ct);
}
