namespace Vortex.Primitives.Events;

/// <summary>A player purchased or renewed Membership / VIP.</summary>
public sealed record MembershipPurchasedEvent(
    int PlayerId,
    int Months,
    bool IsVip,
    bool IsRenewal,
    int CreditCost,
    int TotalMonths
) : IEvent;

/// <summary>Membership gift tokens were granted to a player when their gift cycle elapsed.</summary>
public sealed record MembershipGiftTokenGrantedEvent(int PlayerId, int Tokens, int TotalAvailable)
    : IEvent;

/// <summary>A player claimed a club gift.</summary>
public sealed record MembershipGiftClaimedEvent(int PlayerId, string ProductCode) : IEvent;

/// <summary>A player received their Membership kickback (payday) reward.</summary>
public sealed record MembershipPaydayEvent(int PlayerId, int Credits) : IEvent;

/// <summary>A player's Membership membership lapsed (expired) while their grain was active.</summary>
public sealed record MembershipExpiredEvent(int PlayerId, bool WasVip) : IEvent;
