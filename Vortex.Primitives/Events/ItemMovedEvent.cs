namespace Vortex.Primitives.Events;

/// <summary>A furniture item was moved within a room.</summary>
/// <param name="RotatedInPlace">
/// The item finished on the tile it started on, facing a different way. The client has no separate
/// rotate message -- turning a piece is a move to the same square with the next rotation -- so this
/// is the only thing that tells the two apart, and it is computed here where both the old and the
/// new position are known rather than left for a consumer to reconstruct from <paramref name="Data"/>.
/// A drag that also turns the piece is a move, not a rotation: it changed tile.
/// </param>
public sealed record ItemMovedEvent(
    int ItemId,
    int ActorPlayerId,
    int RoomId,
    string? Data,
    bool RotatedInPlace = false
) : IEvent;
