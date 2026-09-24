using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Vortex.Primitives.Vault;

namespace Vortex.Database.Entities.Players;

/// <summary>
/// One earning in a player's Earnings window. Immutable once written, and never deleted: a claim
/// sets <see cref="ClaimedAt"/>. Deleting would drop <see cref="SourceKey"/> from the unique index,
/// and a deposit replayed after the claim would then be paid a second time.
/// </summary>
[Table("player_vault_income_rewards")]
[Index(nameof(PlayerEntityId), nameof(SourceKey), IsUnique = true)]
public class PlayerVaultIncomeRewardEntity : VortexEntity
{
    [Column("player_id")]
    public int PlayerEntityId { get; set; }

    [Column("reward_category")]
    public int RewardCategory { get; set; }

    [Column("reward_type")]
    public int RewardType { get; set; }

    [Column("amount")]
    public int Amount { get; set; }

    [Column("product_code")]
    public string ProductCode { get; set; } = string.Empty;

    [Column("source_key")]
    [MaxLength(IncomeEarning.MaxSourceKeyLength)]
    public string SourceKey { get; set; } = string.Empty;

    [Column("claimed_at")]
    public DateTime? ClaimedAt { get; set; }

    [ForeignKey(nameof(PlayerEntityId))]
    public PlayerEntity? PlayerEntity { get; set; }
}
