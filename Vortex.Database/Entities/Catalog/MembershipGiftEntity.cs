using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Vortex.Database.Entities.Catalog;

[Table("membership_gifts")]
public class MembershipGiftEntity : VortexEntity
{
    [Column("product_code")]
    [MaxLength(64)]
    public required string ProductCode { get; set; }

    [Column("product_type")]
    [MaxLength(4)]
    public required string ProductType { get; set; }

    [Column("furni_definition_id")]
    public int? FurniDefinitionId { get; set; }

    [Column("extra_param")]
    [MaxLength(256)]
    public string? ExtraParam { get; set; }

    [Column("quantity")]
    [DefaultValue(1)]
    public int Quantity { get; set; } = 1;

    [Column("is_vip")]
    [DefaultValue(false)]
    public bool IsVip { get; set; }

    [Column("sort_order")]
    [DefaultValue(0)]
    public int SortOrder { get; set; }
}
