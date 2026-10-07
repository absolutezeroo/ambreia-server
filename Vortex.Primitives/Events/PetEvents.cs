using Vortex.Primitives.Players;

namespace Vortex.Primitives.Events;

/// <summary>
/// A pet entered the hotel. Adoption is the pet's birth certificate: without it a pet that later
/// changes hands has no first owner on record at all.
/// </summary>
public sealed record PetAdoptedEvent(PlayerId OwnerId, int PetId, string Name, int Type) : IEvent;

/// <summary>A pet was set down in a room.</summary>
/// <remarks>
/// The pet's type is on every one of these events for the same reason: the pet id names one animal
/// in the hotel, and content is written about a species.
/// </remarks>
public sealed record PetPlacedEvent(PlayerId ActorId, int PetId, int Type, int RoomId) : IEvent;

/// <summary>A pet was picked up out of a room and back into an inventory.</summary>
public sealed record PetPickedUpEvent(PlayerId ActorId, int PetId, int Type, int RoomId) : IEvent;

/// <summary>
/// A pet reached a new level. Deliberately the level, not every experience grant: XP moves on every
/// command obeyed, and a record per grant would bury the timeline it is meant to make readable.
/// </summary>
/// <remarks>
/// <paramref name="OwnerId"/> is the pet's owner, not whoever fed it: a level belongs to the pet,
/// and the pet belongs to one account wherever it is standing. Anything crediting a player for this
/// needs that — without it the event names a pet nobody can be paid for, which is exactly why the
/// reward track's <c>pet_level</c> action had nothing to listen to.
/// </remarks>
public sealed record PetLeveledUpEvent(PlayerId OwnerId, int PetId, int Type, int RoomId, int Level)
    : IEvent;
