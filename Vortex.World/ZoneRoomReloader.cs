using System;
using System.Threading.Tasks;
using Vortex.Primitives.Rooms;

namespace Vortex.World;

/// <summary>
/// Tells a zone's room that a new version was published. A delegate so the storage tests can run
/// without a silo; the host wires it to the room grain.
/// </summary>
public sealed class ZoneRoomReloader(Func<RoomId, Task> reload)
{
    public Task ReloadAsync(RoomId roomId) => reload(roomId);
}
