using Vortex.Primitives.Players;

namespace Vortex.Primitives.Events;

/// <summary>
/// A pet entered the hotel. Adoption is the pet's birth certificate: without it a pet that later
/// changes hands has no first owner on record at all.
/// </summary>
public sealed record PetAdoptedEvent(PlayerId OwnerId, int PetId, string Name, int Type) : IEvent;

/// <summary>
/// Two pets produced a baby, which now belongs to <paramref name="OwnerId"/>.
/// </summary>
/// <remarks>
/// Breeding used to publish nothing at all: a baby appeared in an inventory and no progression
/// system, log or dashboard ever heard about it. <paramref name="Type"/> is the baby's own type
/// (24 Baby Bear, 28 Kitten Baby, ...), never the parents'.
/// </remarks>
public sealed record PetBredEvent(PlayerId OwnerId, int PetId, int Type, string Color) : IEvent;

/// <summary>Somebody scratched a pet. <paramref name="ActorId"/> and <paramref name="OwnerId"/>
/// are the two sides the hotel counts separately, and are often the same player.</summary>
/// <remarks>
/// <paramref name="Type"/> is on every one of these events for the same reason: the pet id names one
/// animal in the hotel, and content is written about a species. "Scratch a pet" and "scratch a
/// husky" were the same task until this was carried, because the only thing a filter could say was
/// which individual pet it meant.
/// </remarks>
public sealed record PetRespectedEvent(PlayerId ActorId, PlayerId OwnerId, int PetId, int Type)
    : IEvent;

/// <summary>A player fed a pet by hand. <paramref name="Nutrition"/> is what the item was worth,
/// which is what "giving pets %limit% points of food" counts -- not the number of meals.</summary>
public sealed record PetFedEvent(
    PlayerId ActorId,
    PlayerId OwnerId,
    int PetId,
    int Type,
    int Nutrition
) : IEvent;

/// <summary>A pet was set down in a room.</summary>
public sealed record PetPlacedEvent(PlayerId ActorId, int PetId, int Type, int RoomId) : IEvent;

/// <summary>A pet was picked up out of a room and back into an inventory.</summary>
public sealed record PetPickedUpEvent(PlayerId ActorId, int PetId, int Type, int RoomId) : IEvent;

/// <summary>
/// A pet obeyed an order.
/// </summary>
/// <remarks>
/// Published where the order lands rather than where it is typed: a command reaches the server as
/// ordinary chat — the client has no packet for it — and the words are only an order once the pet
/// has been found, the owner checked, the level met and the energy paid. Anything raised earlier
/// would count a player who said "sit" at a pet that ignored them.
/// </remarks>
public sealed record PetCommandObeyedEvent(
    PlayerId ActorId,
    PlayerId OwnerId,
    int PetId,
    int Type,
    int CommandId
) : IEvent;

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
