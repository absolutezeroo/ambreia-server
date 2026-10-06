using System;

namespace Vortex.Primitives.Events;

/// <summary>A staff member kicked a player out of a room.</summary>
public sealed record PlayerKickedFromRoomEvent(int ActorPlayerId, int TargetPlayerId, int RoomId)
    : IEvent;

/// <summary>A staff member muted a player inside a room for a bounded duration.</summary>
public sealed record PlayerMutedInRoomEvent(
    int ActorPlayerId,
    int TargetPlayerId,
    int RoomId,
    int DurationSeconds
) : IEvent;

/// <summary>A staff member banned a player from a room for a bounded duration.</summary>
public sealed record PlayerBannedInRoomEvent(
    int ActorPlayerId,
    int TargetPlayerId,
    int RoomId,
    int DurationSeconds
) : IEvent;

/// <summary>A staff member sent a moderation alert/caution to a player.</summary>
public sealed record PlayerAlertedEvent(int ActorPlayerId, int TargetPlayerId, int RoomId) : IEvent;

/// <summary>
/// A moderation action was refused because the actor lacked the required capability. Audited as a
/// denied result so attempted privilege escalation stays visible.
/// </summary>
public sealed record ModerationActionDeniedEvent(
    int ActorPlayerId,
    int TargetPlayerId,
    int RoomId,
    string Action
) : IEvent;

/// <summary>
/// A staff member applied the room-tool actions to a room they do not own. Audited as one event
/// rather than three: the client sends the checkboxes together and they are meaningful together.
/// </summary>
public sealed record RoomModeratedByStaffEvent(
    int ActorPlayerId,
    int RoomId,
    bool DoorUnlocked,
    bool NameReset,
    bool UsersKicked
) : IEvent;

/// <summary>
/// A staff member sent a caution or message to every occupant of a room. Audited with the text: a
/// line broadcast to a whole room in the hotel's voice is exactly the kind of thing that has to be
/// answerable for afterwards.
/// </summary>
public sealed record RoomAlertedByStaffEvent(
    int ActorPlayerId,
    int RoomId,
    bool IsCaution,
    string Message
) : IEvent;

/// <summary>A staff member suspended a player's account (null BannedUntil clears the ban).</summary>
public sealed record PlayerAccountBannedEvent(
    int ActorPlayerId,
    int TargetPlayerId,
    DateTime? BannedUntil,
    string Reason
) : IEvent;

/// <summary>
/// A staff member muted a player hotel-wide (null MutedUntil lifts it). Separate from
/// <see cref="PlayerMutedInRoomEvent"/>, which is one room's rule rather than a sanction on the
/// person, and which has a room id this one deliberately has not got.
/// </summary>
public sealed record PlayerHotelMutedEvent(
    int ActorPlayerId,
    int TargetPlayerId,
    DateTime? MutedUntil
) : IEvent;

/// <summary>A staff member trading-locked a player's account (null LockedUntil clears the lock).</summary>
public sealed record PlayerTradingLockedEvent(
    int ActorPlayerId,
    int TargetPlayerId,
    DateTime? LockedUntil
) : IEvent;

/// <summary>
/// A player filed a report. Only the moderator half of a ticket's life was audited -- picked,
/// released, closed -- so the moment somebody actually asked for help left no trace, and neither did
/// an account filing reports as harassment.
/// </summary>
public sealed record CfhTicketOpenedEvent(
    int IssueId,
    int ReporterPlayerId,
    int? ReportedPlayerId,
    int? RoomId,
    int TopicId
) : IEvent;

/// <summary>
/// A moderator claimed CFH tickets. Audited because a claim is the moment a report becomes one
/// person's responsibility: "who was holding this when it went wrong" is otherwise unanswerable.
/// </summary>
public sealed record CfhTicketsPickedEvent(int ActorPlayerId, int[] IssueIds) : IEvent;

/// <summary>A moderator handed CFH tickets back to the queue without resolving them.</summary>
public sealed record CfhTicketsReleasedEvent(int ActorPlayerId, int[] IssueIds) : IEvent;

/// <summary>A moderator closed CFH tickets, with the verdict they closed them under.</summary>
public sealed record CfhTicketsClosedEvent(
    int ActorPlayerId,
    int[] IssueIds,
    string Reason,
    bool Sanctioned
) : IEvent;
