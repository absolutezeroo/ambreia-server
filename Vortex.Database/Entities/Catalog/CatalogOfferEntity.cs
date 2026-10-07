using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;

namespace Vortex.Database.Entities.Catalog;

[Table("catalog_offers")]
public class CatalogOfferEntity : VortexEntity
{
    [Column("page_id")]
    public required int CatalogPageEntityId { get; set; }

    [Column("localization_id")]
    public required string LocalizationId { get; set; }

    [Column("cost_credits")]
    [DefaultValue(0)]
    public required int CostCredits { get; set; }

    [Column("cost_currency")]
    [DefaultValue(0)]
    public required int CostCurrency { get; set; }

    /// <summary>
    /// The activity-point type <see cref="CostCurrency"/> is paid in (0 duckets, 5 diamonds, ...),
    /// NOT a <c>currency_types</c> row id, despite the column's name.
    /// </summary>
    /// <remarks>
    /// Every reader treats it as the type: the purchase debits <c>ActivityPointType = this</c>, the
    /// catalogue wire hands it to the client as the offer's activity-point type, and the importer
    /// (tools/catalog_converter) writes it so for every diamond and ducket offer. It used to carry a
    /// foreign key to <c>currency_types.id</c>, which the imported rows only survived because they
    /// were loaded with FOREIGN_KEY_CHECKS = 0, and which made the dashboard store row ids.
    /// </remarks>
    [Column("currency_type_id")]
    public int? CurrencyTypeId { get; set; }

    [Column("can_gift")]
    [DefaultValue(true)]
    public required bool CanGift { get; set; }

    [Column("can_bundle")]
    [DefaultValue(true)]
    public required bool CanBundle { get; set; }

    [Column("membership_level")]
    [DefaultValue(0)]
    public required int MembershipLevel { get; set; }

    [Column("discount_percent")]
    [DefaultValue(0)]
    public int DiscountPercent { get; set; } = 0;

    [Column("visible")]
    [DefaultValue(true)]
    public required bool Visible { get; set; }

    [ForeignKey(nameof(CatalogPageEntityId))]
    public required CatalogPageEntity Page { get; set; }

    public IList<CatalogProductEntity>? Products { get; set; }
}
