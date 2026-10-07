using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Entities.Players;

namespace Vortex.Database.Entities.Loot;

/// <summary>
/// Records that a player has already taken a once-per-player prize. The welcome gift is the first:
/// it stays in the room after paying out, so without this row the same player would claim it again
/// on every click.
///
/// The unique index is the real guard, not the read that precedes it — two clicks racing would both
/// see "not claimed" if the check alone decided.
/// </summary>
[Table("player_loot_claims")]
[Index(nameof(PlayerEntityId), nameof(LootTableEntityId), IsUnique = true)]
public class PlayerLootClaimEntity : VortexEntity
{
    [Column("player_id")]
    public required int PlayerEntityId { get; set; }

    [Column("pool_id")]
    public required int LootTableEntityId { get; set; }

    [ForeignKey(nameof(PlayerEntityId))]
    public PlayerEntity? PlayerEntity { get; set; }

    [ForeignKey(nameof(LootTableEntityId))]
    public LootTableEntity? LootTableEntity { get; set; }
}
