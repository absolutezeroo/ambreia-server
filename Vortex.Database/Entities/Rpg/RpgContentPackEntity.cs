using System.ComponentModel.DataAnnotations.Schema;

namespace Vortex.Database.Entities.Rpg;

/// <summary>One accepted import (spec Contenu §5): its id is the content version.</summary>
[Table("rpg_content_packs")]
public class RpgContentPackEntity : VortexEntity
{
    [Column("account_id")]
    public required int AccountId { get; set; }

    [Column("added")]
    public required int Added { get; set; }

    [Column("changed")]
    public required int Changed { get; set; }

    [Column("removed")]
    public required int Removed { get; set; }

    /// <summary>The pack's migrations.json as sent (content authoring §3): the latest one is exported.</summary>
    [Column("migrations", TypeName = "longtext")]
    public string? Migrations { get; set; }
}
