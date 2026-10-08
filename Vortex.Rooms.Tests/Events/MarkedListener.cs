using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Events;
using Vortex.Primitives.Rooms.Grains;

namespace Vortex.Rooms.Tests.Events;

/// <summary>
/// Top-level rather than nested inside the test class on purpose: the scan takes <c>IsPublic</c>
/// types, and a nested type is never <c>IsPublic</c> however it is declared. Nesting these would
/// have them skipped for the wrong reason and the test would prove nothing.
/// </summary>
[RoomEventListener]
public sealed class MarkedListener(IRoomGrain room) : IRoomEventListener
{
    public IRoomGrain Room { get; } = room;

    public Task OnRoomEventAsync(RoomEvent evt, CancellationToken ct) => Task.CompletedTask;
}
