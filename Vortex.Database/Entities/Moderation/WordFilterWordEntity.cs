using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Vortex.Primitives.Moderation.WordFilter;

namespace Vortex.Database.Entities.Moderation;

/// <summary>
/// One word on the hotel-wide filter. Staff own the list through the dashboard; the live copy is the
/// compiled set inside <c>WordFilter</c>, reloaded after every write.
/// </summary>
/// <remarks>
/// <see cref="Key"/> is the uniqueness, not <see cref="Word"/>: "Coonnn" and "c.o.n" are one word to
/// the matcher, so they must be one row. It is <c>WordFilterNormalizer.Key(Word)</c>, written by the
/// admin service. A unique index over live rows only would need a filtered index MySQL does not
/// have, so the admin service revives a soft-deleted row with the same key instead of inserting.
/// </remarks>
[Table("word_filter_words")]
[Index(nameof(Key), IsUnique = true)]
public class WordFilterWordEntity : VortexEntity
{
    [Column("word")]
    [MaxLength(WordFilterConfig.WordMaxLength)]
    public required string Word { get; set; }

    [Column("word_key")]
    [MaxLength(WordFilterConfig.WordMaxLength)]
    public required string Key { get; set; }

    [Column("action")]
    public required WordFilterAction Action { get; set; }

    [Column("match")]
    public required WordFilterMatch Match { get; set; }

    /// <summary>Null means the configured default (<c>word_filter.replacement</c>).</summary>
    [Column("replacement")]
    [MaxLength(WordFilterConfig.WordMaxLength)]
    public string? Replacement { get; set; }

    /// <summary>The operator's e-mail, as the dashboard's operation runner records actors.</summary>
    [Column("created_by")]
    [MaxLength(255)]
    public string? CreatedBy { get; set; }
}
