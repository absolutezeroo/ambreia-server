namespace Vortex.Primitives.Players;

/// <summary>
/// The membership's live tunables, served from <c>IServerConfigGrain</c>; each default is the
/// fallback when a key has no admin override stored in the DB. Here rather than beside the
/// membership grain because the packet layer reads the kickback rate too, and it may not reference
/// a platform project.
/// </summary>
public static class MembershipConfigKeys
{
    /// <summary>Badge code granted on the first Membership purchase.</summary>
    public const string MembershipBadgeCodeKey = "membership.badge_code";
    public const string MembershipBadgeCodeDefault = "HC1";

    /// <summary>Badge code granted while holding a VIP membership.</summary>
    public const string VipBadgeCodeKey = "membership.vip_badge_code";
    public const string VipBadgeCodeDefault = "HC2";

    /// <summary>Length in days of one gift / payday cycle (also caps the streak payday bonus).</summary>
    public const string GiftCycleDaysKey = "membership.gift_cycle_days";
    public const int GiftCycleDaysDefault = 31;

    /// <summary>
    /// Grace window in days after expiry within which a renewal keeps the membership streak.
    /// A longer lapse resets the streak so it starts over from the new purchase.
    /// </summary>
    public const string StreakGraceDaysKey = "membership.streak_grace_days";
    public const int StreakGraceDaysDefault = 7;

    /// <summary>Percentage of credits spent this period returned as the Membership kickback (payday).</summary>
    public const string KickbackPercentKey = "membership.kickback_percent";
    public const int KickbackPercentDefault = 10;
}
