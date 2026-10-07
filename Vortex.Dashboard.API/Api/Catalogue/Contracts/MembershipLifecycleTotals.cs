namespace Vortex.Dashboard.API.Api.Catalogue.Contracts;

/// <summary>
/// The window's three events, counted.
/// </summary>
/// <param name="RenewalShare">Renewals over purchases plus renewals, 0 when there were neither.
/// Expiries are deliberately out of it: the question is how many of the club's sales were repeat
/// business, and a lapse is not a sale.</param>
public sealed record MembershipLifecycleTotals(
    int Purchases,
    int Renewals,
    int Expired,
    double RenewalShare
);
