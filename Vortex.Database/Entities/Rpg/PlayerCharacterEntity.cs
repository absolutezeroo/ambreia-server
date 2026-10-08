using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Entities.Players;

namespace Vortex.Database.Entities.Rpg;

/// <summary>
/// The RPG state of an avatar (spec Personnage §7): written by <c>PlayerCharacterGrain</c> alone.
/// A row exists only for an avatar that has a class. Its class ids are rewritten by an import's
/// replace rule, never by the grain.
/// </summary>
[Table("player_characters")]
[Index(nameof(PlayerEntityId), IsUnique = true)]
public class PlayerCharacterEntity : VortexEntity
{
    [Column("player_id")]
    public required int PlayerEntityId { get; set; }

    [Column("class_id")]
    [MaxLength(ContentIdLength)]
    public required string ClassId { get; set; }

    [Column("specialization_id")]
    [MaxLength(ContentIdLength)]
    public required string SpecializationId { get; set; }

    [Column("level")]
    [DefaultValue(1)]
    public int Level { get; set; } = 1;

    /// <summary>XP toward the next level; 0 at the max level.</summary>
    [Column("xp")]
    [DefaultValue(0)]
    public int Xp { get; set; }

    /// <summary>The world zone the character last entered (never a dungeon copy).</summary>
    [Column("last_room_id")]
    public int? LastRoomId { get; set; }

    [ForeignKey(nameof(PlayerEntityId))]
    public PlayerEntity? PlayerEntity { get; set; }
}
