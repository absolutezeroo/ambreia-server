using System.Collections.Generic;

namespace Vortex.Primitives.Events;

/// <summary>A furniture item was placed from inventory into a room.</summary>
/// <param name="IsWallItem">
/// Whether it went on a wall rather than the floor. The coarse type an operator means by "a floor
/// item", which no definition id expresses on its own.
/// </param>
/// <param name="LogicName">The definition's logic key, e.g. <c>furniture_floor_hole</c>.</param>
/// <param name="KindsInRoom">
/// Distinct definitions in the room after the placement.
/// </param>
/// <param name="SameLogicInRoom">
/// Items in the room sharing <paramref name="LogicName"/>, this one included. Counts only the room
/// knows, so they ride on the event; nothing reads either today.
/// </param>
public sealed record ItemPlacedEvent(
    int ItemId,
    int ActorPlayerId,
    int OwnerId,
    int RoomId,
    string? Data,
    int DefinitionId = 0,
    bool IsWallItem = false,
    string LogicName = "",
    int KindsInRoom = 0,
    int SameLogicInRoom = 0
) : IEvent;

/// <summary>A wallpaper, floor or landscape item was consumed into a room's decoration.</summary>
/// <param name="Property">The room property it set: <c>wallpaper</c>, <c>floor</c> or <c>landscape</c>.</param>
public sealed record RoomDecoratedEvent(int PlayerId, int RoomId, string Property) : IEvent;

/// <summary>
/// A player stepped onto a piece of floor furniture.
/// </summary>
/// <remarks>
/// Published from the room's walk tick, so every floor item raises it, for players only. It fires
/// very often -- once per tile walked onto -- and nothing subscribes today, so it is a publish into
/// an empty handler list. A subscriber added later must stay that cheap.
/// </remarks>
public sealed record PlayerWalkedOnFurniEvent(
    int PlayerId,
    int ItemId,
    int DefinitionId,
    int RoomId
) : IEvent;

/// <summary>
/// A player stood beside a piece of floor furniture, or on it.
/// </summary>
/// <remarks>
/// <para>
/// One per piece of furniture around the tile, so a step in a furnished room raises several. The
/// tile is scanned on every step; like <see cref="PlayerWalkedOnFurniEvent"/>, it stays affordable
/// only because nothing subscribes today.
/// </para>
/// <para>
/// <paramref name="Relation"/> is measured from the furniture, never from the room — see
/// <c>RelativePositions</c>. <paramref name="Figure"/> rides along so that everything this one
/// signal claims is true at the same instant: "barefoot, in front of the television" read from two
/// signals is two moments, and the shoes can go back on in between.
/// </para>
/// <para>
/// <paramref name="Posture"/> rides along for exactly that reason. "Seated in front of it" split
/// across two signals stops being one: a player can sit down, stand up and walk over, and satisfy
/// both.
/// </para>
/// </remarks>
public sealed record PlayerStoodNearFurniEvent(
    int PlayerId,
    int ItemId,
    int DefinitionId,
    int RoomId,
    string Relation,
    string Figure,
    string Posture = ""
) : IEvent;

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

/// <summary>A furniture item was picked up from a room back into an inventory.</summary>
public sealed record ItemPickedUpEvent(
    int ItemId,
    int ActorPlayerId,
    int FromOwnerId,
    int ToOwnerId,
    int RoomId
) : IEvent;
