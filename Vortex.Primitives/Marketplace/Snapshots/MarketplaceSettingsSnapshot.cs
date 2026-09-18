using Orleans;

namespace Vortex.Primitives.Marketplace.Snapshots;

[GenerateSerializer, Immutable]
public sealed record MarketplaceSettingsSnapshot
{
    [Id(0)]
    public required int CommissionPercent { get; init; }

    [Id(1)]
    public required int OfferDurationSeconds { get; init; }

    [Id(2)]
    public required int AveragePricePeriodDays { get; init; }

    [Id(3)]
    public required int HalfTaxLimit { get; init; }
}
