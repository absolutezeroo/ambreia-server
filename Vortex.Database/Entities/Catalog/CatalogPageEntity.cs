using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Vortex.Primitives.Catalog;
using Vortex.Primitives.Catalog.Enums;

namespace Vortex.Database.Entities.Catalog;

[Table("catalog_pages")]
public class CatalogPageEntity : VortexEntity
{
    /// <summary>Which catalog tree this page belongs to (see CatalogSnapshotProvider, which filters
    /// by this column). Only the Normal tree is left.</summary>
    [Column("catalog_type")]
    [DefaultValue(CatalogType.Normal)]
    public required CatalogType CatalogType { get; set; }

    [Column("parent_id")]
    public int? ParentEntityId { get; set; }

    [Column("localization")]
    [MaxLength(50)]
    public required string Localization { get; set; }

    [Column("name")]
    [MaxLength(50)]
    public string? Name { get; set; }

    [Column("icon")]
    [DefaultValue(0)]
    public required int Icon { get; set; }

    [Column("layout")]
    [DefaultValue(CatalogPageLayout.Default3x3)]
    public required CatalogPageLayout Layout { get; set; }

    [Column("image_data")]
    public List<string>? ImageData { get; set; } = null!;

    [Column("text_data")]
    public List<string>? TextData { get; set; } = null!;

    [Column("sort_order")]
    [DefaultValue(0)]
    public required int SortOrder { get; set; }

    [Column("visible")]
    [DefaultValue(true)]
    public required bool Visible { get; set; }

    [ForeignKey(nameof(ParentEntityId))]
    public CatalogPageEntity? ParentEntity { get; set; }

    public IList<CatalogPageEntity>? Children { get; set; }

    public IList<CatalogOfferEntity>? Offers { get; set; }
}
