namespace Vortex.Dashboard.API.Api.Catalogue.Contracts;

/// <summary>
/// The subscription population as it stands.
/// </summary>
/// <param name="ExpiringIn7Days">A subset of the active ones, not a separate group.</param>
/// <param name="ActiveRate">Active over total, 0 when there are none.</param>
public sealed record MembershipTotals(
    int TotalSubscriptions,
    int ActiveSubscriptions,
    int InactiveSubscriptions,
    int ExpiringIn7Days,
    int ExpiringIn30Days,
    double ActiveRate
);
