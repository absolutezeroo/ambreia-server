using Orleans;

namespace Vortex.Primitives.Catalog;

[GenerateSerializer, Immutable]
public sealed record MembershipOffer
{
    [Id(0)]
    public required int OfferId { get; init; }

    [Id(1)]
    public required string ProductCode { get; init; }

    [Id(2)]
    public required int PriceCredits { get; init; }

    [Id(3)]
    public required int PriceActivityPoints { get; init; }

    [Id(4)]
    public required int PriceActivityPointType { get; init; }

    [Id(5)]
    public required bool IsVip { get; init; }

    [Id(6)]
    public required int Months { get; init; }

    [Id(7)]
    public required int ExtraDays { get; init; }

    [Id(8)]
    public required bool IsGiftable { get; init; }

    [Id(9)]
    public required int DaysLeftAfterPurchase { get; init; }

    [Id(10)]
    public required int Year { get; init; }

    [Id(11)]
    public required int Month { get; init; }

    [Id(12)]
    public required int Day { get; init; }
}
