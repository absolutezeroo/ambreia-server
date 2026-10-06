using System.Collections.Generic;

namespace Vortex.Dashboard.API.Api.Hotel.Contracts;

/// <summary>What the hotel's pets are, and how many appeared over the reported window.</summary>
public sealed record PetStats(
    ReportWindow Window,
    PetTotals Totals,
    IReadOnlyList<PetTypeCount> ByType,
    IReadOnlyList<PetRaceCount> ByRace,
    IReadOnlyList<PetGrowthPoint> Growth,
    IReadOnlyList<PetOwnerCount> TopOwners
);
