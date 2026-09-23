using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Vortex.Primitives.Moderation.WordFilter;

namespace Vortex.Database.Entities.Moderation;

/// <summary>
/// One time the hotel-wide filter caught something. Written in batches by the durable observability
/// writer, read by staff in the dashboard's journal.
/// </summary>
[Table("word_filter_hits")]
[Index(nameof(PlayerEntityId), nameof(OccurredAt))]
[Index(nameof(OccurredAt))]
public class WordFilterHitEntity : VortexEntity
{
    /// <summary>
    /// The player — or, for a name refused while the web API creates an avatar, the account: there
    /// is no player yet at that point.
    /// </summary>
    [Column("player_id")]
    public required int PlayerEntityId { get; set; }

    [Column("source")]
    public required WordFilterSource Source { get; set; }

    /// <summary>The word that decided the outcome. No foreign key: a deleted word keeps its history.</summary>
    [Column("word_id")]
    public required int WordId { get; set; }

    [Column("action")]
    public required WordFilterAction Action { get; set; }

    [Column("original_text")]
    [MaxLength(WordFilterConfig.JournalTextMaxLength)]
    public required string OriginalText { get; set; }

    /// <summary>When the player wrote it — capture time, not flush time.</summary>
    [Column("occurred_at")]
    public required DateTime OccurredAt { get; set; }
}
