namespace Vortex.Dashboard.API.Api.Catalogue.Contracts;

/// <summary>How each length of subscription sold, renewed and lapsed.</summary>
public sealed record MembershipMonthsBreakdown(
    int Months,
    int Total,
    int Purchases,
    int Renewals,
    int Expired
);
