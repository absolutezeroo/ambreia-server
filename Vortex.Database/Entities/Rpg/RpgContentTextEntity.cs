using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Vortex.Database.Entities.Rpg;

/// <summary>The text file of one language (spec Contenu §7), as imported.</summary>
[Table("rpg_content_texts")]
[Index(nameof(Language), IsUnique = true)]
public class RpgContentTextEntity : VortexEntity
{
    [Column("language")]
    [MaxLength(8)]
    public required string Language { get; set; }

    [Column("body", TypeName = "longtext")]
    public required string Body { get; set; }
}
