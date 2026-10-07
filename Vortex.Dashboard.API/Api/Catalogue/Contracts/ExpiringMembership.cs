using System;

namespace Vortex.Dashboard.API.Api.Catalogue.Contracts;

/// <summary>A subscription about to lapse. The name is null for a deleted player.</summary>
public sealed record ExpiringMembership(
    int PlayerId,
    string? PlayerName,
    string Type,
    int Level,
    int TotalMonths,
    DateTime ExpiresAt,
    double RemainingDays
);
