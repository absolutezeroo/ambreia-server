using System.Collections.Generic;

namespace Vortex.Dashboard.API.Api.Progression.Contracts;

/// <summary>
/// What players hold across the three reward surfaces: badges, effects and outfits.
/// </summary>
/// <param name="EffectImageTemplate">The grant forms name ids and codes that may not exist yet, so
/// they build their own preview from these rather than looking one up. Null when the hotel has no
/// renderer configured for that kind.</param>
public sealed record PlayerRewardStats(
    PlayerRewardTotals Totals,
    string? EffectImageTemplate,
    string? BadgeImageTemplate,
    IReadOnlyList<BadgeHolderCount> TopBadges,
    IReadOnlyList<EffectOwnerCount> TopEffects,
    IReadOnlyList<BadgeCollector> TopCollectors
);
