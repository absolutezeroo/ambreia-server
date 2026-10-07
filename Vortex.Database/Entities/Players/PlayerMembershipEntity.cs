using System;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Vortex.Primitives.Players.Enums;

namespace Vortex.Database.Entities.Players;

[Table("player_memberships")]
[Index(nameof(PlayerEntityId), nameof(MembershipType), IsUnique = true)]
public class PlayerMembershipEntity : VortexEntity
{
    [Column("player_id")]
    public required int PlayerEntityId { get; set; }

    [Column("membership_type")]
    public required MembershipType MembershipType { get; set; }

    [Column("level")]
    public int Level { get; set; } = 1;

    [Column("expires_at")]
    public DateTime ExpiresAt { get; set; }

    [Column("total_months")]
    public int TotalMonths { get; set; } = 0;

    [Column("gifts_available")]
    public int GiftsAvailable { get; set; } = 0;

    [Column("next_gift_at")]
    public DateTime? NextGiftAt { get; set; }

    [Column("past_membership_days")]
    public int PastMembershipDays { get; set; } = 0;

    [Column("past_vip_days")]
    public int PastVipDays { get; set; } = 0;

    [Column("first_subscribed_at")]
    public DateTime? FirstSubscribedAt { get; set; }

    [Column("last_expired_at")]
    public DateTime? LastExpiredAt { get; set; }

    [Column("hc_badge_granted")]
    public bool HcBadgeGranted { get; set; } = false;

    [ForeignKey(nameof(PlayerEntityId))]
    public required PlayerEntity PlayerEntity { get; set; }
}
