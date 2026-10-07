using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Entities.Furniture;
using Vortex.Database.Entities.Messenger;
using Vortex.Database.Entities.Room;
using Vortex.Database.Entities.Security;
using Vortex.Primitives.Players.Avatar;
using Vortex.Primitives.Players.Enums;
using Vortex.Primitives.Rooms.Enums;

namespace Vortex.Database.Entities.Players;

[Table("players")]
[Index(nameof(Name), IsUnique = true)]
public class PlayerEntity : VortexEntity
{
    [Column("account_id")]
    public int? PlayerAccountEntityId { get; set; }

    [ForeignKey(nameof(PlayerAccountEntityId))]
    public PlayerAccountEntity? PlayerAccount { get; set; }

    [Column("name")]
    public required string Name { get; set; }

    [Column("motto")]
    public string? Motto { get; set; }

    [Column("figure")]
    [MaxLength(FigureString.MaxLength)]
    [DefaultValue("hr-115-42.hd-195-19.ch-3030-82.lg-275-1408.fa-1201.ca-1804-64")]
    public required string Figure { get; set; }

    [Column("gender")]
    [DefaultValue(AvatarGenderType.Male)]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public required AvatarGenderType Gender { get; set; }

    [Column("status")]
    [DefaultValue(PlayerStatusType.Offline)]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public required PlayerStatusType PlayerStatus { get; set; }

    [Column("perk_flags")]
    [DefaultValue(PlayerPerkFlags.None)]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public required PlayerPerkFlags PlayerPerks { get; set; }

    /// <summary>Stamped on every successful SSO login. Null for an account that has never logged in
    /// since the column was introduced. The staff mod tool's user card is the only reader — it shows
    /// "minutes since last login", which is meaningless without a persisted timestamp.</summary>
    [Column("last_login_at")]
    public DateTime? LastLoginAt { get; set; }

    /// <summary>Denormalised total achievement score (sum of completed levels' score points),
    /// surfaced on the profile without activating the achievement grain. Kept in sync by
    /// <c>PlayerAchievementGrain</c> on progression.</summary>
    [Column("achievement_score")]
    [DefaultValue(0)]
    public int AchievementScore { get; set; }

    // The player's favourite guild, surfaced on the guild badge in the UI. Plain scalar (no FK
    // navigation) to avoid yet another circular relationship through groups/rooms.
    [Column("favourite_group_id")]
    public int? FavouriteGroupId { get; set; }

    /// <summary>Null = not locked. Player-level (not account-level): trading is a gameplay
    /// restriction on this profile, not a login/identity concern. Checked by the (not yet built)
    /// trading system.</summary>
    [Column("trading_locked_until")]
    public DateTime? TradingLockedUntil { get; set; }

    /// <summary>
    /// Hotel-wide chat mute expiry, or null when never muted. Distinct from <c>room_mutes</c>, which
    /// a room owner sets and which stops at that room's door: this one is a staff sanction and has
    /// to follow the player into whatever room they walk into next.
    /// </summary>
    [Column("muted_until")]
    public DateTime? MutedUntil { get; set; }

    /// <summary>Null = the new-user flow has not been completed yet. Drives the
    /// <c>AVATAR_NAME_CHANGE</c> suggested login action: the client shows its look-and-name
    /// onboarding on every login until this is stamped.</summary>
    [Column("nux_completed_at")]
    public DateTime? NuxCompletedAt { get; set; }

    /// <summary>
    /// Whether this avatar's web profile — its badges, friends, apparts and groups — is readable by
    /// anyone, or only by its owner.
    /// </summary>
    /// <remarks>
    /// <para>
    /// False by default, because that is what the website already promises in habbo.com's own words
    /// on the registration form: "Ton profil est privé par défaut. Il faut cocher la case pour le
    /// rendre public." The column arrived after those words did, so every profile was public while
    /// the page said otherwise.
    /// </para>
    /// <para>
    /// It governs the WEB profile only. Nothing on the game socket reads it: a room still shows who
    /// is in it, and the friend list still shows a friend. Hiding a player from the client is a
    /// different feature with a different surface, and calling this one "private" for that too would
    /// be the same promise broken a second time.
    /// </para>
    /// </remarks>
    [Column("profile_visible")]
    public bool ProfileVisible { get; set; }

    [InverseProperty("PlayerEntity")]
    public List<PlayerBadgeEntity>? PlayerBadges { get; set; }

    [InverseProperty("PlayerEntity")]
    public List<PlayerCurrencyEntity>? PlayerCurrencies { get; set; }

    [InverseProperty("PlayerEntity")]
    public List<FurnitureEntity>? Furniture { get; set; }

    [InverseProperty("PlayerEntity")]
    public List<MessengerCategoryEntity>? MessengerCategories { get; set; }

    [InverseProperty("PlayerEntity")]
    public List<MessengerFriendEntity>? MessengerFriends { get; set; }

    [InverseProperty("RequestedPlayerEntity")]
    public List<MessengerRequestEntity>? MessengerRequests { get; set; }

    [InverseProperty("PlayerEntity")]
    public List<MessengerRequestEntity>? MessengerRequestsSent { get; set; }

    [InverseProperty("PlayerEntity")]
    public List<MessengerBlockedEntity>? MessengerBlocked { get; set; }

    [InverseProperty("PlayerEntity")]
    public List<MessengerIgnoredEntity>? MessengerIgnored { get; set; }

    [InverseProperty("PlayerEntity")]
    public List<SecurityTicketEntity>? SecurityTickets { get; set; }

    [InverseProperty("PlayerEntity")]
    public List<RoomEntity>? Rooms { get; set; }

    [InverseProperty("PlayerEntity")]
    public List<RoomBanEntity>? RoomBans { get; set; }

    [InverseProperty("PlayerEntity")]
    public List<RoomMuteEntity>? RoomMutes { get; set; }

    [InverseProperty("PlayerEntity")]
    public List<RoomRightEntity>? RoomRights { get; set; }

    [InverseProperty("PlayerEntity")]
    public List<RoomChatlogEntity>? RoomChatlogs { get; set; }

    [InverseProperty("PlayerEntity")]
    public List<PlayerSubscriptionEntity>? PlayerSubscriptions { get; set; }
}
