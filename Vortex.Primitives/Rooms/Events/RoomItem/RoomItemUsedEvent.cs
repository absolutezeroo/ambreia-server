namespace Vortex.Primitives.Rooms.Events.RoomItem;

/// <summary>
/// Raised when a player actively uses (activates / toggles) a floor item — the double-click "use"
/// action — as opposed to a state change of any cause (<see cref="RoomItemStateChangedEvent"/>).
/// <c>CausedBy</c> carries the using player.
/// </summary>
public sealed record RoomItemUsedEvent : RoomItemEvent;
