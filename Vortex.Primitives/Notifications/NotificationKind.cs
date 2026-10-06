namespace Vortex.Primitives.Notifications;

/// <summary>
/// What a notification is about. One value per thing a player is told, never one per theme: the
/// website words each kind on its own, and an operator trimming the noise turns off a kind rather
/// than a whole domain.
/// </summary>
/// <remarks>
/// <para>
/// The numbering is grouped by theme with gaps, so a theme can grow without renumbering — the
/// value is what is stored in <c>web_notifications.kind</c>, and a renumber would silently
/// relabel every row already written.
/// </para>
/// <para>
/// Nearly all of these are raised from events the hotel already publishes; the handlers under
/// <c>Vortex.WebApi/Notifications/</c> only subscribe. The three feed kinds are the exception and
/// their events were added with them.
/// </para>
/// <para>
/// In <c>Vortex.Primitives</c> rather than beside those handlers only because
/// <c>WebNotificationEntity</c> stores it, and <c>Vortex.Database</c> cannot reference the web
/// API. The notifications themselves are the website's.
/// </para>
/// </remarks>
public enum NotificationKind
{
    // ---- Club, gifts and rewards -----------------------------------------------------------
    ClubExpired = 0,
    ClubPayday = 1,
    ClubGiftToken = 2,

    // 3 was RewardTrackCompleted, removed in the RPG fork (M0). Persisted: never reuse it.
    RaffleWon = 4,
    VoucherRedeemed = 5,

    // ---- Social and groups -----------------------------------------------------------------
    Blocked = 10,
    FriendRequestReceived = 11,
    FriendRequestAccepted = 12,
    RespectReceived = 13,
    GroupMembershipAccepted = 14,
    GroupMembershipRejected = 15,
    GroupKicked = 16,

    /// <remarks>
    /// The one kind sent to SEVERAL people at once — every admin of the group, plus its owner.
    /// They are the only ones who can act on it, and none of them was there when it happened.
    /// </remarks>
    GroupJoinRequested = 17,

    // ---- Moderation, about me --------------------------------------------------------------
    Muted = 20,
    TradingLocked = 21,
    CfhClosed = 22,
    RoomModerated = 23,

    // ---- Economy and items -----------------------------------------------------------------
    MarketplaceSold = 30,
    GiftReceived = 31,
    RentalExpired = 32,

    // ---- Progression -----------------------------------------------------------------------
    /// <remarks>
    /// The loudest kind by far — a level lands several times an hour for an active player. It is
    /// its own value precisely so it can be switched off without taking quests and badges with it.
    /// </remarks>
    AchievementLevelUp = 40,
    QuestCompleted = 41,
    BadgeGranted = 42,

    // 43 was HabbiconCollectionCompleted, removed in the RPG fork (M0). Persisted: never reuse it.
    PrizeAwarded = 44,

    // ---- The website's feed ----------------------------------------------------------------
    FeedMentioned = 50,
    FeedCommented = 51,
    FeedReposted = 52,
    FeedFavourited = 53,

    // ---- Account security ------------------------------------------------------------------
    /// <remarks>
    /// These tell a player what they did not do themselves. A bell is the weaker channel for that
    /// — habbo.com mails them, and this hotel should too one day — but a notification the thief
    /// cannot delete is still worth more than nothing.
    /// </remarks>
    PasswordChanged = 60,
    MfaChanged = 61,
    NameChanged = 62,

    // ---- My room ---------------------------------------------------------------------------
    // 70 was the room-rated notification, removed with room rating (M0). Not renumbered.
    RoomRightsGranted = 71,
    RoomRaidDetected = 72,
}
