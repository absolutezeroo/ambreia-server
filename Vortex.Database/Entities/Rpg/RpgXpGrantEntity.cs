using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Entities.Players;

namespace Vortex.Database.Entities.Rpg;

/// <summary>
/// An XP grant already made by an operator (spec Personnage §7, audit P0-6): written before the XP
/// it gives, so a replayed request finds its key and gives nothing more, and summed per day for the
/// cap on what an operator can hand out. At most once: a process lost between this row and the XP
/// write keeps the key and loses the grant; a grant the character could not take hands its key back.
/// </summary>
[Table("rpg_xp_grants")]
[Index(nameof(GrantKey), IsUnique = true)]
[Index(nameof(PlayerEntityId), nameof(CreatedAt))]
public class RpgXpGrantEntity : VortexEntity
{
    public const int GrantKeyLength = 128;

    public const int SourceLength = 64;

    [Column("grant_key")]
    [MaxLength(GrantKeyLength)]
    public required string GrantKey { get; set; }

    [Column("player_id")]
    public required int PlayerEntityId { get; set; }

    [ForeignKey(nameof(PlayerEntityId))]
    public PlayerEntity? PlayerEntity { get; set; }

    [Column("amount")]
    public required int Amount { get; set; }

    /// <summary>Who gave it, as the route names the actor ("admin:42").</summary>
    [Column("source")]
    [MaxLength(SourceLength)]
    public required string Source { get; set; }
}
