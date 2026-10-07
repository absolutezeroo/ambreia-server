using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Vortex.Database.Entities;

public class VortexEntity
{
    /// <summary>
    /// Cap for a short, human-written content identifier that takes part in an index. No entity
    /// uses it at the moment.
    /// </summary>
    /// <remarks>
    /// An uncapped string becomes <c>varchar(512)</c> in utf8mb4, which is 2048 bytes, and MySQL
    /// refuses a composite index over more than 3072 of them. A composite unique index across two
    /// content ids — which is exactly what makes a claim unrepeatable — therefore does not exist
    /// unless the columns are bounded. 64 characters is far more than any of these identifiers
    /// needs and leaves the widest such index well inside the ceiling.
    /// </remarks>
    protected const int ContentIdLength = 64;

    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("created_at")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at")]
    [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
    public DateTime UpdatedAt { get; set; }

    [Column("deleted_at")]
    public DateTime? DeletedAt { get; set; }
}
