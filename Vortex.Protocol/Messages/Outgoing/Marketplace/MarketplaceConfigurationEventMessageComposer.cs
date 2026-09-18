using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Marketplace;

/// <summary>
/// Field names are the official client's, taken from the parser's own accessors
/// (<c>_SafePkg_1932/_SafeCls_1990.as</c> in WIN63-202607011411-782849652). They used to be guesses
/// — <c>Credits</c> and <c>Advertisements</c> for what the client reads as a token batch, and
/// <c>OfferTime</c>/<c>DisplayTime</c> for two fields in different units — and the guesses hid a
/// live bug for as long as they stood.
/// </summary>
[GenerateSerializer, Immutable]
public sealed record MarketplaceConfigurationEventMessageComposer : IComposer
{
    [Id(0)]
    public required bool Enabled { get; init; }

    [Id(1)]
    public required int Commission { get; init; }

    [Id(2)]
    public required int TokenBatchPrice { get; init; }

    [Id(3)]
    public required int TokenBatchSize { get; init; }

    [Id(4)]
    public required int OfferMinPrice { get; init; }

    [Id(5)]
    public required int OfferMaxPrice { get; init; }

    /// <summary>Hours, not seconds. The client divides it by 24 to say how many days an offer
    /// lasts (<c>MarketplaceView.as:152</c>).</summary>
    [Id(6)]
    public required int ExpirationHours { get; init; }

    /// <summary>Days. Substituted into "average price over the last %days% days".</summary>
    [Id(7)]
    public required int AveragePricePeriod { get; init; }

    [Id(8)]
    public required int SellingFeePercentage { get; init; }

    [Id(9)]
    public required int RevenueLimit { get; init; }

    /// <summary>Divisor in the client's fee preview. Must be non-zero.</summary>
    [Id(10)]
    public required int HalfTaxLimit { get; init; }
}
