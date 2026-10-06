using System;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Vortex.Primitives.Players.Enums;

namespace Vortex.Database.Entities.Players;

[Table("player_subscriptions")]
[Index(nameof(PlayerEntityId), nameof(SubscriptionType), IsUnique = true)]
public class PlayerSubscriptionEntity : VortexEntity
{
    [Column("player_id")]
    public required int PlayerEntityId { get; set; }

    [Column("subscription_type")]
    public required SubscriptionType SubscriptionType { get; set; }

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

    [Column("past_club_days")]
    public int PastClubDays { get; set; } = 0;

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
