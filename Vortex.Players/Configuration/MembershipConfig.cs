namespace Vortex.Players.Configuration;

/// <summary>
/// Strongly-typed options for Membership / VIP. Only the maintenance timer interval stays bound via
/// IOptions (read once at grain activation to register the infra timer). The tunable membership
/// economics and granted badge codes are served live from <c>IServerConfigGrain</c>, keyed by
/// <see cref="Primitives.Players.MembershipConfigKeys" />.
/// </summary>
public sealed class MembershipConfig
{
    public const string SECTION_NAME = "Vortex:Membership";

    /// <summary>Interval between hourly maintenance runs (expiry, gift tokens, kickback payout).</summary>
    public int MaintenanceIntervalMs { get; init; } = 3_600_000;
}
