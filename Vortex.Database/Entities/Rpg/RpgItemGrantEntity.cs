using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Entities.Players;

namespace Vortex.Database.Entities.Rpg;

/// <summary>
/// A grant already made (spec Objets §5, §7): written in the same transaction as the items it gave,
/// so a replayed grant finds its key and gives nothing more.
/// </summary>
[Table("rpg_item_grants")]
[Index(nameof(GrantKey), IsUnique = true)]
public class RpgItemGrantEntity : VortexEntity
{
    public const int GrantKeyLength = 128;

    [Column("grant_key")]
    [MaxLength(GrantKeyLength)]
    public required string GrantKey { get; set; }

    [Column("player_id")]
    public required int PlayerEntityId { get; set; }

    [ForeignKey(nameof(PlayerEntityId))]
    public PlayerEntity? PlayerEntity { get; set; }
}
