using System;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Enums;
using Vortex.Primitives.Rooms.Enums;

namespace Vortex.Players.Grains;

public sealed class PlayerLiveState
{
    public required PlayerId PlayerId { get; init; }
    public string Name { get; set; } = string.Empty;
    public string Motto { get; set; } = string.Empty;
    public string Figure { get; set; } = string.Empty;
    public AvatarGenderType Gender { get; set; } = AvatarGenderType.Male;

    /// <summary>
    /// The account's entitlements. Cached here as well as stored because a perk gates what the
    /// player may do, and an authorization flag that only exists in the database is one a live
    /// session never sees change.
    /// </summary>
    public PlayerPerkFlags Perks { get; set; } = PlayerPerkFlags.None;
    public int AchievementScore { get; set; } = 0;
    public int RespectReceived { get; set; } = 0;
    public int RespectGivenToday { get; set; } = 0;
    public int PetRespectGivenToday { get; set; } = 0;
    public int RespectReplenishesUsedToday { get; set; } = 0;
    public DateTime? RespectResetDate { get; set; } = null;

    /// <summary>Guild whose badge this player shows on their avatar, or 0 for none. Owned here:
    /// every write goes through <c>PlayerGrain.SetFavouriteGroupAsync</c> so the cached value, the
    /// database and the badge on the avatar in the room never drift apart.</summary>
    public int FavouriteGroupId { get; set; } = 0;
    public string FavouriteGroupName { get; set; } = string.Empty;

    /// <summary>Hotel-wide mute expiry, cached so the room can read it off the entry snapshot
    /// instead of asking the database on the chat path.</summary>
    public DateTime? MutedUntil { get; set; } = null;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    /// <summary>Null until the new-user flow has been completed; see PlayerEntity.NuxCompletedAt.</summary>
    public DateTime? NuxCompletedAt { get; set; } = null;

    /// <summary>
    /// The owning ACCOUNT's safety lock; see PlayerAccountEntity.SafetyLocked. While it is set the
    /// player cannot spend, and every spending handler asks this rather than the database.
    /// </summary>
    public bool SafetyLocked { get; set; }

    /// <summary>
    /// Whether the player counts as staff to a room: holds <c>room.moderate.any</c>, which the
    /// seeded moderator, admin and owner roles carry and the player and economy roles do not. Held
    /// in state, and resolved once at load for the same reason as <see cref="SafetyLocked"/> — it
    /// rides every player summary, and a role lookup per summary would mean a service call on
    /// every room entry and every look change.
    /// </summary>
    public bool IsModerator { get; set; }

    public int ClubLevel { get; set; } = 0;
    public DateTime ClubExpiresAt { get; set; } = DateTime.MinValue;
    public int ClubTotalMonths { get; set; } = 0;
    public int ClubGiftsAvailable { get; set; } = 0;
    public DateTime? ClubNextGiftAt { get; set; } = null;
    public int ClubPastClubDays { get; set; } = 0;
    public int ClubPastVipDays { get; set; } = 0;
    public DateTime? ClubFirstSubscribedAt { get; set; } = null;
    public DateTime? ClubLastExpiredAt { get; set; } = null;
    public bool ClubBadgeGranted { get; set; } = false;

    public DateTime? KickbackPaydayAt { get; set; } = null;
    public int KickbackCreditsSpent { get; set; } = 0;
    public int KickbackTotalRewarded { get; set; } = 0;
    public int KickbackTotalSpent { get; set; } = 0;
}
