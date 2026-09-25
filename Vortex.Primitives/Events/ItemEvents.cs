using System.Collections.Generic;

namespace Vortex.Primitives.Events;

/// <summary>A furniture item came into existence (catalog grant, LTD win, staff grant, ...).
/// <paramref name="Data"/> carries optional source-specific JSON detail.</summary>
public sealed record ItemCreatedEvent(int ItemId, int OwnerId, string? Data) : IEvent;

/// <summary>Two players opened a trade window. The trade-level lifecycle (<c>started</c> →
/// <c>completed</c>/<c>cancelled</c>) is audited under the <c>Item</c> category for dashboard analytics
/// and fraud review; individual item moves are recorded separately as <see cref="ItemTradedEvent"/>.</summary>
public sealed record TradeStartedEvent(int PlayerOneId, int PlayerTwoId, int RoomId) : IEvent;

/// <summary>
/// Raised when both sides have confirmed but before ownership is re-validated and swapped, and
/// published cancellably: a behaviour that sets <c>Cancel</c> aborts the commit exactly as a failed
/// ownership re-validation does, and both participants get the trade back rather than a broken one.
/// </summary>
public sealed record TradeCompletingEvent(
    int PlayerOneId,
    int PlayerTwoId,
    IReadOnlyList<int> PlayerOneItemIds,
    IReadOnlyList<int> PlayerTwoItemIds,
    int RoomId
) : IEvent;

/// <summary>Two players completed a trade and items changed hands atomically.</summary>
public sealed record TradeCompletedEvent(
    int PlayerOneId,
    int PlayerTwoId,
    IReadOnlyList<int> PlayerOneItemIds,
    IReadOnlyList<int> PlayerTwoItemIds,
    int RoomId
) : IEvent;

/// <summary>A trade ended without an exchange. <paramref name="Reason"/> distinguishes a plain
/// cancellation, a confirmation-time decline, a participant leaving, and a commit-time failure.</summary>
public sealed record TradeCancelledEvent(
    int PlayerOneId,
    int PlayerTwoId,
    int RoomId,
    string Reason
) : IEvent;

/// <summary>A furniture item was placed from inventory into a room.</summary>
/// <param name="IsWallItem">
/// Whether it went on a wall rather than the floor. The coarse type an operator means by "a floor
/// item", which no definition id expresses on its own.
/// </param>
/// <param name="LogicName">The definition's logic key, e.g. <c>furniture_floor_hole</c>.</param>
/// <param name="KindsInRoom">
/// Distinct definitions in the room after the placement — "a room with %limit% different furni".
/// </param>
/// <param name="SameLogicInRoom">
/// Items in the room sharing <paramref name="LogicName"/>, this one included — "%limit% floor hole
/// furni". Counts only the room knows, so they ride on the event.
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
/// Published from the base floor logic, so every floor item raises it. It fires very often -- once
/// per tile walked onto -- which is why the reward-track bridge checks its action index before it
/// does anything at all: with no content listening, this costs one hash lookup.
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
/// One per piece of furniture around the tile, so a step in a furnished room raises several. That
/// is affordable for exactly one reason: like <see cref="PlayerWalkedOnFurniEvent"/>, the tile is
/// only scanned at all once the interest gate has said some content names the action. With none,
/// a step costs one hash lookup and no scan happens.
/// </para>
/// <para>
/// <paramref name="Relation"/> is measured from the furniture, never from the room — see
/// <c>RelativePositions</c>. <paramref name="Figure"/> rides along because the whole point of
/// answering this in one signal is that everything it claims is true at the same instant: "barefoot,
/// in front of the television" written as two steps is two moments, and the shoes can go back on in
/// between.
/// </para>
/// <para>
/// <paramref name="Posture"/> rides along for exactly that reason. "Seated in front of it" is the
/// shape Habbo's own events ask for, and split across two steps it stops being one: a player can
/// sit down, stand up and walk over, and satisfy both.
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

/// <summary>A staff member rewrote a placed item's stored row through the in-client furni editor.
/// Audited separately from <see cref="ItemMovedEvent"/> and <see cref="ItemTradedEvent"/> on
/// purpose: those record actions an ordinary player can take, this one records a privileged
/// out-of-band write (<c>room.furni.edit</c>) and is the only trace that an item's owner or
/// definition was changed without a trade or a purchase. <paramref name="Data"/> carries the before
/// and after values of every field the edit touched.</summary>
public sealed record ItemStaffEditedEvent(
    int ItemId,
    int ActorPlayerId,
    int RoomId,
    string Fields,
    string? Data
) : IEvent;

/// <summary>A furniture item changed owner through a completed player-to-player trade.
/// <paramref name="ActorPlayerId"/> is the trade participant giving the item away.</summary>
public sealed record ItemTradedEvent(
    int ItemId,
    int ActorPlayerId,
    int FromOwnerId,
    int ToOwnerId,
    int RoomId
) : IEvent;

/// <summary>A furniture item was picked up from a room back into an inventory.</summary>
public sealed record ItemPickedUpEvent(
    int ItemId,
    int ActorPlayerId,
    int FromOwnerId,
    int ToOwnerId,
    int RoomId
) : IEvent;

/// <summary>
/// Why a furniture row stopped existing. Once <c>DeletedAt</c> is stamped the row keeps no trace of
/// what spent it, so this is the only thing that tells a cracked egg from a binned sticky in the
/// forensics trail.
/// </summary>
public enum ItemDeletionReason
{
    Binned,
    CreditRedeemed,
    PresentOpened,
    Cracked,
    MysteryBoxOpened,
    MysteryTrophyOpened,
    PetFoodUsedUp,
    MonsterplantSeedPlanted,
}

/// <summary>
/// A furniture item was permanently destroyed (consumed by the room, cracked, opened, ...).
/// </summary>
public sealed record ItemDeletedEvent(
    int ItemId,
    int OwnerId,
    int? ActorPlayerId,
    ItemDeletionReason Reason
) : IEvent;

/// <summary>
/// A wrapped present was opened. The parcel is consumed and the contents land in the opener's
/// inventory as brand-new items, so without this record the chain from "who sent it" to "what came
/// out" breaks exactly at the interesting point.
/// </summary>
public sealed record PresentOpenedEvent(long ItemId, int ActorPlayerId, int RoomId, int OfferId)
    : IEvent;
