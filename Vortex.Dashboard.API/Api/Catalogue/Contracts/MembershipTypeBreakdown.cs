namespace Vortex.Dashboard.API.Api.Catalogue.Contracts;

/// <summary>
/// One subscription type, counted and averaged.
/// </summary>
/// <param name="AverageRemainingDays">Over the active ones only, 0 when none is active.</param>
/// <param name="AverageTotalMonths">Over every subscription of this type, expired included: it is
/// how long people stay, which an expired subscription answers as well as a live one.</param>
public sealed record MembershipTypeBreakdown(
    string Type,
    int Total,
    int Active,
    int Inactive,
    double AverageRemainingDays,
    double AverageTotalMonths
);
