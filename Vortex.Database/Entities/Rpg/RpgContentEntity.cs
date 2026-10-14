using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Vortex.Database.Entities.Rpg;

/// <summary>One content definition (spec Contenu §5): its document as imported, and its hash.</summary>
[Table("rpg_content")]
[Index(nameof(ContentId), IsUnique = true)]
public class RpgContentEntity : VortexEntity
{
    [Column("content_id")]
    [MaxLength(ContentIdLength)]
    public required string ContentId { get; set; }

    [Column("kind")]
    [MaxLength(32)]
    public required string Kind { get; set; }

    [Column("document", TypeName = "longtext")]
    public required string Document { get; set; }

    /// <summary>SHA-256 of the re-serialized document, in hex: an import changes only what differs.</summary>
    [Column("hash", TypeName = "char(64)")]
    public required string Hash { get; set; }

    /// <summary>The pack that last wrote this row.</summary>
    [Column("pack_id")]
    public required int PackId { get; set; }

    [ForeignKey(nameof(PackId))]
    public RpgContentPackEntity? Pack { get; set; }

    /// <summary>The file it came from, relative to content/ (content authoring §3): where an export
    /// writes it back. Null for a row imported before sources were kept.</summary>
    [Column("source_path")]
    [MaxLength(256)]
    public string? SourcePath { get; set; }

    /// <summary>The file's text as authored, $schema included: what an export writes, so the
    /// repository gets its files back unchanged.</summary>
    [Column("source", TypeName = "longtext")]
    public string? Source { get; set; }
}
