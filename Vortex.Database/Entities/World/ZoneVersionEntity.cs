using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Vortex.Database.Entities.World;

/// <summary>One saved document of a zone; versions only grow, nothing is deleted.</summary>
[Table("zone_versions")]
[Index(nameof(ZoneEntityId), nameof(Version), IsUnique = true)]
public class ZoneVersionEntity : VortexEntity
{
    [Column("zone_id")]
    public required int ZoneEntityId { get; set; }

    [Column("version")]
    public required int Version { get; set; }

    [Column("schema_version")]
    public required int SchemaVersion { get; set; }

    [Column("document", TypeName = "longtext")]
    public required string Document { get; set; }

    [Column("author_account_id")]
    public required int AuthorAccountId { get; set; }

    [Column("status")]
    public required ZoneVersionStatus Status { get; set; }

    [Column("comment")]
    [MaxLength(512)]
    public string Comment { get; set; } = string.Empty;
}
