using System.Collections.Generic;

namespace Vortex.Dashboard.API.Api.Progression.Contracts;

/// <summary>Everything one player holds, in one call, for the investigation flow.</summary>
public sealed record PlayerRewardDetail(
    int PlayerId,
    string PlayerName,
    string? AvatarUrl,
    IReadOnlyList<PlayerBadgeRow> Badges,
    IReadOnlyList<PlayerEffectRow> Effects,
    IReadOnlyList<PlayerOutfitRow> Outfits
);
