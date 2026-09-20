using System;
using System.Collections.Immutable;
using Vortex.Primitives.Players;

namespace Vortex.Primitives.Events;

/// <summary>Internal lifecycle event when a player session is attached to a game session.</summary>
public sealed record PlayerConnectedEvent(PlayerId PlayerId, DateTime ConnectedAtUtc) : IEvent;

/// <summary>Internal lifecycle event when a player session is detached from a game session.</summary>
public sealed record PlayerDisconnectedEvent(
    PlayerId PlayerId,
    DateTime ConnectedAtUtc,
    DateTime DisconnectedAtUtc,
    long SessionDurationSeconds
) : IEvent;

/// <summary>
/// Raised before a player is let into a room, and published cancellably: a behaviour that sets
/// <c>Cancel</c> refuses the entry, and the client is told the same way a locked door tells it.
/// Every other check the room does — ban, capacity, password, doorbell — has already passed.
/// </summary>
public sealed record PlayerEnteringRoomEvent(PlayerId PlayerId, int RoomId) : IEvent;

/// <summary>Player entered a room for a tracked user journey.</summary>
/// <param name="OwnerId">
/// Who owns the room. Carried so that "join their flat" is expressible without a follow event: a
/// reward-track step can match this against the player an earlier step captured.
/// </param>
public sealed record PlayerEnteredRoomEvent(
    PlayerId PlayerId,
    int RoomId,
    DateTime EnteredAtUtc,
    int OwnerId = 0
) : IEvent;

/// <summary>
/// Raised before a chat line reaches anyone, and published cancellably: cancelling drops the line
/// silently. It carries the text but cannot rewrite it — the publisher reads only <c>Cancel</c>, so
/// a behaviour that edits the message would be changing something nobody reads back.
/// <para>
/// Covers whispers too (<paramref name="TargetPlayerId" /> is set for those), unlike the room-local
/// <c>PlayerChatEvent</c> the wired keyword trigger listens on, which is public chat only.
/// </para>
/// </summary>
public sealed record PlayerChattingEvent(
    PlayerId PlayerId,
    int RoomId,
    string Message,
    PlayerId? TargetPlayerId
) : IEvent;

/// <summary>
/// A chat line was accepted and sent to the room. The <em>after</em> to
/// <see cref="PlayerChattingEvent"/>'s before, and the one anything that rewards talking must hang
/// off: the pre-event fires for a line a behaviour then cancels, so counting it would pay for words
/// nobody heard.
/// </summary>
/// <param name="Whisper">True for a whisper, which reaches one person rather than the room.</param>
/// <param name="Text">
/// What was actually said. Carried because "say something with the word X in it" is a task an
/// operator writes and an id cannot express, and because the room already has the line in hand when
/// it raises this — enriching here costs nothing, while reading it back later would cost everything.
/// </param>
public sealed record PlayerChattedEvent(
    PlayerId PlayerId,
    int RoomId,
    bool Whisper,
    string Text = ""
) : IEvent;

/// <summary>
/// An avatar performed a gesture the room accepted — a dance, a wave. Raised by the room after the
/// avatar actually changed, so a dance refused because the player is sitting is not one.
/// </summary>
/// <param name="Gesture">
/// <c>dance</c> or <c>wave</c>. One event with a discriminator rather than one per gesture: they
/// are the same act, and the next one added should not need a new type.
/// </param>
/// <param name="Style">
/// Which one of it, for a gesture that has more than one: <c>dance</c>, <c>pogo_mogo</c>,
/// <c>duck_funk</c> or <c>the_rollie</c>. Empty for a wave, which has no variants.
/// </param>
/// <remarks>
/// The style is carried rather than resolved later because the room is the only place that knows
/// it: by the time anything downstream sees the event, the avatar is simply dancing. Content that
/// wants "do the Rollie" needs it here or not at all.
/// </remarks>
public sealed record PlayerGesturedEvent(
    PlayerId PlayerId,
    int RoomId,
    string Gesture,
    string Style = ""
) : IEvent;

/// <summary>
/// A private message was accepted and delivered. After the friend and block rules, so a message
/// refused for either is not one.
/// </summary>
/// <param name="HabbiconId">The Habbicon it was, or 0 for a text message.</param>
public sealed record MessengerMessageSentEvent(
    PlayerId PlayerId,
    PlayerId ReceiverId,
    int HabbiconId
) : IEvent;

/// <summary>Player left a room for a tracked user journey.</summary>
public sealed record PlayerLeftRoomEvent(
    PlayerId PlayerId,
    int RoomId,
    DateTime LeftAtUtc,
    long RoomDurationSeconds
) : IEvent;

/// <summary>Player changed their motto.</summary>
public sealed record PlayerMottoChangedEvent(PlayerId PlayerId, string Motto) : IEvent;

/// <summary>Player changed their avatar figure (look).</summary>
public sealed record PlayerFigureChangedEvent(PlayerId PlayerId, string Figure) : IEvent;

/// <summary>
/// A player completed one or more levels of an achievement. Raised once per level-up, after the row
/// and every reward have landed, so a record here means the player really holds the badge.
/// </summary>
public sealed record AchievementLevelUpEvent(
    PlayerId PlayerId,
    int AchievementId,
    string AchievementName,
    int Level,
    string BadgeCode,
    int ScoreGained
) : IEvent;

/// <summary>
/// A player was granted a badge they did not already hold. Re-grants are silent: the grain returns
/// early on an owned badge, so one record here means one badge actually entered the collection.
/// </summary>
public sealed record BadgeGrantedEvent(PlayerId PlayerId, string BadgeCode) : IEvent;

/// <summary>
/// A player changed which badges they wear. Carries the full new selection ordered by slot, because
/// the interesting question is what they chose to show, not which slot moved.
/// </summary>
public sealed record BadgesEquippedEvent(PlayerId PlayerId, ImmutableArray<string> BadgeCodes)
    : IEvent;

/// <summary>
/// A player finished a quest. Raised once per completion, after the reward has been granted, so a
/// re-run of the same progress pass never doubles the record.
/// </summary>
public sealed record QuestCompletedEvent(
    PlayerId PlayerId,
    int QuestId,
    string CampaignCode,
    string LocalizationCode,
    int RewardType,
    int RewardAmount
) : IEvent;

/// <summary>A player accepted a quest and made it their active one.</summary>
public sealed record QuestAcceptedEvent(
    PlayerId PlayerId,
    int QuestId,
    string CampaignCode,
    string LocalizationCode
) : IEvent;

/// <summary>
/// A player dropped a quest. <paramref name="Rejected"/> separates turning one down outright from
/// abandoning the one already in progress -- the same grain path, but not the same intent.
/// </summary>
public sealed record QuestAbandonedEvent(
    PlayerId PlayerId,
    int QuestId,
    string CampaignCode,
    string LocalizationCode,
    bool Rejected
) : IEvent;

/// <summary>
/// A player activated an avatar effect from their inventory. Activation is the moment the effect
/// starts burning its duration, and the moment it becomes visible to everyone else.
/// </summary>
public sealed record AvatarEffectActivatedEvent(
    PlayerId PlayerId,
    int EffectId,
    int DurationSeconds
) : IEvent;

/// <summary>
/// A survey was finished. Deliberately the completion and not each answer: a survey is answered one
/// question at a time, and a record per question would drown the timeline it belongs to.
/// </summary>
public sealed record PollCompletedEvent(PlayerId PlayerId, int PollId, string PollCode) : IEvent;

/// <summary>A survey was declined before it was started.</summary>
public sealed record PollRejectedEvent(PlayerId PlayerId, int PollId) : IEvent;

/// <summary>A completed daily task paid out. Only once per task: a second click never reaches here.</summary>
public sealed record DailyTaskClaimedEvent(PlayerId PlayerId, int TaskId) : IEvent;

/// <summary>
/// A wardrobe slot was saved. The figure is kept because a saved outfit is a look the account can
/// return to in one click -- which is what makes it worth having on record next to
/// <see cref="PlayerFigureChangedEvent"/> rather than only the look currently worn.
/// </summary>
public sealed record WardrobeOutfitSavedEvent(PlayerId PlayerId, int SlotId, string Figure)
    : IEvent;

/// <summary>
/// A resolution challenge on a statue was won and its badge handed out. The statue is named because
/// the challenge lives on a piece of furniture that can be traded away afterwards.
/// </summary>
public sealed record AchievementResolutionWonEvent(
    PlayerId PlayerId,
    int ItemId,
    int AchievementId,
    int TargetLevel,
    string BadgeCode
) : IEvent;

/// <summary>A resolution challenge still in progress was cleared off its statue.</summary>
public sealed record AchievementResolutionResetEvent(PlayerId PlayerId, int ItemId) : IEvent;

/// <summary>
/// A client preference was saved. <paramref name="Setting"/> names the pane -- sound, chat, room
/// invites, camera, UI flags -- because turning a setting off is sometimes the first move of an
/// incident, and one undifferentiated "preferences changed" line cannot show which.
/// </summary>
public sealed record AccountPreferenceChangedEvent(PlayerId PlayerId, string Setting) : IEvent;

/// <summary>A clothing item was redeemed off a furni and unlocked permanently on the account.</summary>
public sealed record ClothingRedeemedEvent(PlayerId PlayerId, int ItemId) : IEvent;

/// <summary>A player answered a quiz.</summary>
public sealed record QuizSubmittedEvent(PlayerId PlayerId, string QuizCode) : IEvent;
