using System;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Vortex.Database.Entities.Players;

[Table("player_membership_kickbacks")]
[Index(nameof(PlayerEntityId), IsUnique = true)]
public class PlayerMembershipKickbackEntity : VortexEntity
{
    [Column("player_id")]
    public required int PlayerEntityId { get; set; }

    [Column("payday_at")]
    public DateTime? PaydayAt { get; set; }

    [Column("credits_spent")]
    public int CreditsSpent { get; set; } = 0;

    [Column("total_rewarded")]
    public int TotalRewarded { get; set; } = 0;

    [Column("total_spent")]
    public int TotalSpent { get; set; } = 0;

    [ForeignKey(nameof(PlayerEntityId))]
    public required PlayerEntity PlayerEntity { get; set; }
}
