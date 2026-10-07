using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Vortex.Primitives.Rpg.Content;

namespace Vortex.Database.Entities.Rpg;

/// <summary>A migration rule an import applied (spec Contenu §8), kept as history.</summary>
[Table("rpg_content_migrations")]
public class RpgContentMigrationEntity : VortexEntity
{
    [Column("from_content_id")]
    [MaxLength(ContentIdLength)]
    public required string FromContentId { get; set; }

    [Column("action")]
    [DefaultValue(ContentMigrationAction.Replace)]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public required ContentMigrationAction Action { get; set; }

    [Column("to_content_id")]
    [MaxLength(ContentIdLength)]
    public string? ToContentId { get; set; }

    [Column("pack_id")]
    public required int PackId { get; set; }

    [ForeignKey(nameof(PackId))]
    public RpgContentPackEntity? Pack { get; set; }
}
