using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Events;

namespace Vortex.Rooms.Tests.Events;

/// <summary>Implements the interface without the attribute, like the room's own systems do. Public
/// and top-level for the same reason as <see cref="MarkedListener"/>.</summary>
public sealed class UnmarkedListener : IRoomEventListener
{
    public Task OnRoomEventAsync(RoomEvent evt, CancellationToken ct) => Task.CompletedTask;
}
