using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Vortex.Database.Entities.Catalog;

[Table("membership_offers")]
public class MembershipOfferEntity : VortexEntity
{
    [Column("product_code")]
    [MaxLength(64)]
    public required string ProductCode { get; set; }

    [Column("price_credits")]
    [DefaultValue(0)]
    public required int PriceCredits { get; set; }

    [Column("price_activity_points")]
    [DefaultValue(0)]
    public required int PriceActivityPoints { get; set; }

    [Column("price_activity_point_type")]
    [DefaultValue(0)]
    public required int PriceActivityPointType { get; set; }

    [Column("is_vip")]
    [DefaultValue(false)]
    public required bool IsVip { get; set; }

    [Column("months")]
    [DefaultValue(1)]
    public required int Months { get; set; }

    [Column("extra_days")]
    [DefaultValue(0)]
    public required int ExtraDays { get; set; }

    [Column("is_giftable")]
    [DefaultValue(true)]
    public required bool IsGiftable { get; set; }

    [Column("sort_order")]
    [DefaultValue(0)]
    public required int SortOrder { get; set; }
}
