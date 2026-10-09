using System;
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
    /// <summary>Room for a few slots of <c>"skill:&lt;id&gt;"</c>: the bar's size is configured.</summary>
    private const int ActionBarLength = 1024;

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

    /// <summary>Health the character had when it was last written (spec M1a §7); null: full.</summary>
    [Column("current_health")]
    public int? CurrentHealth { get; set; }

    /// <summary>Mana the character had when it was last written; null: full.</summary>
    [Column("current_mana")]
    public int? CurrentMana { get; set; }

    /// <summary>When health and mana were last set: out of a zone, they regenerate from there.</summary>
    /// <summary>The action bar (spec M1b §9): a JSON array of action ids, "" for an empty slot; null
    /// for a bar never set. Written by the character grain alone.</summary>
    [Column("action_bar")]
    [MaxLength(ActionBarLength)]
    public string? ActionBar { get; set; }

    [Column("vitals_at")]
    public DateTime? VitalsAt { get; set; }

    [ForeignKey(nameof(PlayerEntityId))]
    public PlayerEntity? PlayerEntity { get; set; }
}
