using Vortex.Primitives.Players;

namespace Vortex.Primitives.Events;

/// <summary>
/// Raised before a player is let into a room, and published cancellably: a behaviour that sets
/// <c>Cancel</c> refuses the entry, and the client is told the same way a locked door tells it.
/// Every other check the room does — capacity, locked door, password — has already passed.
/// <para>
/// Handlers run inside the room's turn (<c>RoomGrain.AdmitAsync</c>), which the player's presence
/// turn is waiting on: a handler must not call that room or that player's presence grain (it would
/// wait out the call timeout), and the room tick waits for it. Decide from the event and return.
/// </para>
/// </summary>
public sealed record PlayerEnteringRoomEvent(PlayerId PlayerId, int RoomId) : IEvent;
