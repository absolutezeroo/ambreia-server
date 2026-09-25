using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Vortex.Primitives.Catalog.Enums;

namespace Vortex.Database.Entities.Catalog;

/// <summary>
/// One choice the gift dialog offers. A parcel row names its furniture in
/// <see cref="FurnitureName"/>; a box or ribbon row gives its art index in <see cref="ArtIndex"/>.
/// </summary>
[Table("catalog_gift_wrappings")]
public class CatalogGiftWrappingEntity : VortexEntity
{
    [Column("kind")]
    public required GiftWrappingKind Kind { get; set; }

    /// <summary>Parcel rows only: the furniture class name, e.g. <c>present_gen3</c>.</summary>
    [Column("furniture_name")]
    [MaxLength(ContentIdLength)]
    public string? FurnitureName { get; set; }

    /// <summary>
    /// Box and ribbon rows only: the index the client turns into
    /// <c>catalog.gift_wrapping_new.box.&lt;n&gt;</c> and its sprite.
    /// </summary>
    [Column("art_index")]
    public int? ArtIndex { get; set; }

    /// <summary>
    /// Parcel rows only: offered as the dialog's "default" box, the one that wraps with no box or
    /// ribbon art. The client picks one at random when several are flagged.
    /// </summary>
    [Column("is_default")]
    [DefaultValue(false)]
    public bool IsDefault { get; set; }

    [Column("sort_order")]
    [DefaultValue(0)]
    public int SortOrder { get; set; }
}
