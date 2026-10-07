namespace Vortex.Players.RewardVault;

/// <summary>
/// Operator switches for the Earnings window, served live from <c>IServerConfigGrain</c> and listed
/// in <c>ConfigKeyCatalog</c>. One per category that has a live source — a switch for a category
/// nothing pays into would be offered, flipped, and do nothing.
/// </summary>
public static class EarningsConfig
{
    /// <summary>Achievement level-up credits and duckets wait in the Earnings window (true) or land in the wallet (false).</summary>
    public const string AchievementsToRewardVaultKey = "earnings.achievements.to_vault";

    /// <summary>The Membership payday waits in the Earnings window (true) or lands in the wallet (false).</summary>
    public const string MembershipToRewardVaultKey = "earnings.membership.to_vault";

    public const bool ToRewardVaultDefault = true;
}
