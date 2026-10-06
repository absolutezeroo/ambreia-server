using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Vortex.Database.Entities.Prizes;

/// <summary>
/// A named set of weighted rewards. Whatever hands something out at random draws from one of these,
/// so a new seasonal pool is an operator insert rather than a code change.
/// </summary>
[Table("prize_pools")]
[Index(nameof(Code), IsUnique = true)]
public class PrizePoolEntity : VortexEntity
{
    /// <summary>Stable identifier the server draws by. Renaming a pool must not break the code that
    /// draws from it, which is why the draw key is this and not <see cref="Name"/>.</summary>
    [Column("code")]
    [MaxLength(64)]
    public required string Code { get; set; }

    /// <summary>Operator-facing label, free to change.</summary>
    [Column("name")]
    [MaxLength(128)]
    public required string Name { get; set; }

    /// <summary>
    /// Comma-separated list of the variants entries of this pool may be restricted to; empty means
    /// the pool is free-form. The mystery box pool lists the eight client-renderable key colours, so
    /// an entry typed with a colour the client cannot tint is treated as "any variant" at load time
    /// instead of becoming permanently undrawable.
    /// </summary>
    [Column("variants")]
    [MaxLength(512)]
    [DefaultValue("")]
    public string Variants { get; set; } = string.Empty;

    [Column("enabled")]
    [DefaultValue(true)]
    public bool Enabled { get; set; } = true;

    [Column("notes")]
    [MaxLength(512)]
    [DefaultValue("")]
    public string Notes { get; set; } = string.Empty;
}
