using Vortex.Primitives.Rooms.Events;

namespace Vortex.Rooms.Tests.Events;

/// <summary>A room event of the test's own: the periodic one it borrowed left with the cut.</summary>
internal sealed record ProbeRoomEvent : RoomEvent;
