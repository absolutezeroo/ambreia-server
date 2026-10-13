using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Entities.Players;

namespace Vortex.Database.Entities.Rpg;

/// <summary>
/// One quest of a character's log (spec M2a §3.3): taken, under way or handed in. Owned by the quest
/// log grain, which reads them on activation and writes the log on each change; an abandoned quest's
/// row is deleted.
/// </summary>
[Table("rpg_character_quests")]
[Index(nameof(PlayerEntityId), nameof(QuestId), IsUnique = true)]
public class RpgCharacterQuestEntity : VortexEntity
{
    public const int StatusLength = 16;

    public const int ProgressLength = 256;

    [Column("player_id")]
    public required int PlayerEntityId { get; set; }

    [ForeignKey(nameof(PlayerEntityId))]
    public PlayerEntity? PlayerEntity { get; set; }

    [Column("quest_id")]
    [MaxLength(ContentIdLength)]
    public required string QuestId { get; set; }

    /// <summary>active, ready or done.</summary>
    [Column("status")]
    [MaxLength(StatusLength)]
    public required string Status { get; set; }

    /// <summary>One counter per objective, in order: "3,0,1".</summary>
    [Column("progress")]
    [MaxLength(ProgressLength)]
    public required string Progress { get; set; }

    [Column("accepted_at")]
    public required DateTime AcceptedAt { get; set; }

    [Column("completed_at")]
    public DateTime? CompletedAt { get; set; }
}
