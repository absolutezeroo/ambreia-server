using System;
using Orleans;

namespace Vortex.Primitives.Orleans.Snapshots.Players;

[GenerateSerializer, Immutable]
public sealed record MembershipSnapshot
{
    [Id(0)]
    public bool IsActive { get; init; }

    [Id(1)]
    public bool IsVip { get; init; }

    [Id(2)]
    public DateTime ExpiresAt { get; init; }

    [Id(3)]
    public int DaysLeft { get; init; }

    [Id(4)]
    public int TotalMonths { get; init; }

    [Id(5)]
    public int GiftsAvailable { get; init; }

    [Id(6)]
    public DateTime? NextGiftAt { get; init; }

    [Id(7)]
    public DateTime? PaydayAt { get; init; }

    [Id(8)]
    public int CreditsSpentThisPeriod { get; init; }

    [Id(9)]
    public int TotalCreditsRewarded { get; init; }

    [Id(10)]
    public int TotalCreditsSpent { get; init; }

    [Id(11)]
    public int PastMembershipDays { get; init; }

    [Id(12)]
    public int PastVipDays { get; init; }
}
