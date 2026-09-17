using System;

namespace Vortex.Dashboard.API.Api.Progression.Contracts;

/// <summary>
/// A Relic waiting to be collected.
/// </summary>
/// <remarks>Only outstanding claims are listed: one already taken in full is history, not a reward.</remarks>
public sealed record NftClaimRow(
    int Id,
    int PlayerId,
    string? PlayerName,
    string ProductCode,
    string SetId,
    string Collection,
    int ClaimLimit,
    int ClaimedAmount,
    int Remaining,
    DateTime? ValidFrom,
    DateTime? ValidTo,
    int MinCollectorLevel,
    bool IsNft,
    string? IconUrl
);
