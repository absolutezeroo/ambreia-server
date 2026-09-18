using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;

namespace Vortex.Database.Entities.Marketplace;

/// <summary>
/// Singleton row (the first/only row in the table) holding admin-editable marketplace tunables.
/// Defaults match the values previously hardcoded in <c>MarketplacePurchaseGrain</c> and
/// <c>GetMarketplaceConfigurationMessageHandler</c>, so seeding this row changes nothing until an
/// operator edits it.
/// </summary>
[Table("marketplace_settings")]
public class MarketplaceSettingsEntity : VortexEntity
{
    [Column("commission_percent")]
    [DefaultValue(1)]
    public int CommissionPercent { get; set; } = 1;

    [Column("offer_duration_seconds")]
    [DefaultValue(259200)]
    public int OfferDurationSeconds { get; set; } = 259200;

    /// <summary>
    /// The window the client names in "average price over the last %days% days"
    /// (MarketPlaceCatalogWidget.as:651). Display only — nothing on the server reads it.
    /// </summary>
    [Column("average_price_period_days")]
    [DefaultValue(7)]
    public int AveragePricePeriodDays { get; set; } = 7;

    /// <summary>
    /// The divisor in the fee the client previews when a player lists an item:
    /// <c>price * (sellingFee / 100 + 0.5 * price / halfTaxLimit)</c> (MarketplaceView.as:380).
    /// It must never be zero — the client divides by it unguarded. Kept far above any plausible
    /// price so the progressive half of that formula stays negligible and the preview matches the
    /// flat <see cref="CommissionPercent"/> the purchase grain actually charges. Lower it only
    /// alongside a purchase grain that charges the same curve, or the preview starts lying again.
    /// </summary>
    [Column("half_tax_limit")]
    [DefaultValue(1_000_000_000)]
    public int HalfTaxLimit { get; set; } = 1_000_000_000;
}
