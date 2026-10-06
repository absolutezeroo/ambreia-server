using Vortex.Primitives.Players;

namespace Vortex.Primitives.Events;

/// <summary>A player created a room.</summary>
/// <remarks>
/// Carries what the creation form said, not just the id it produced: a subscriber's most useful
/// question about a new room is what kind of room it is, and the id of a room that did not exist a
/// moment ago answers nothing. Reward-track tasks filter on these — "build a flat in Chill", "build
/// one on the tower model" — and reading them back per event would be a database round trip for
/// values the caller already has in hand.
/// </remarks>
/// <param name="CategoryId">The navigator category, or 0 when the creator picked none.</param>
public sealed record RoomCreatedEvent(
    PlayerId OwnerId,
    int RoomId,
    string Name,
    string Description,
    string ModelName,
    int CategoryId
) : IEvent;

/// <summary>
/// The owner deleted their room. Soft-deleted in the database, but gone as far as anyone can see --
/// and the furniture that was in it moved somewhere, which is usually the real question.
/// </summary>
public sealed record RoomDeletedEvent(PlayerId ActorId, int RoomId, string Name) : IEvent;

/// <summary>
/// The room's settings were saved. <paramref name="Section"/> says which dialog did it -- the main
/// settings, the navigator category and trade mode, or the tags. One event rather than three because
/// they are the same act; the section becomes part of the action name so each stays filterable.
/// </summary>
public sealed record RoomSettingsUpdatedEvent(
    PlayerId ActorId,
    int RoomId,
    string Name,
    string Section
) : IEvent;

/// <summary>
/// Rights in a room changed hands. <paramref name="Change"/> is the verb (granted, removed,
/// removed_all, gave_up) and <paramref name="TargetPlayerId"/> is null for the room-wide ones.
/// Recorded because rights are how a room gets emptied by someone who never owned it.
/// </summary>
public sealed record RoomRightsChangedEvent(
    PlayerId ActorId,
    int RoomId,
    PlayerId? TargetPlayerId,
    string Change
) : IEvent;

/// <summary>
/// Somebody rang a locked room's doorbell and a rights holder answered. Recorded because a doorbell
/// is the one place a player who was never in the room still interacts with the people inside, and
/// a refusal leaves nothing else behind at all.
/// </summary>
public sealed record RoomDoorbellAnsweredEvent(
    PlayerId ActorId,
    PlayerId TargetPlayerId,
    int RoomId,
    bool Admitted
) : IEvent;
