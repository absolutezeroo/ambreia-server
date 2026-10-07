namespace Vortex.Dashboard.API.Api.Progression.Contracts;

/// <summary>
/// The three surfaces counted.
/// </summary>
/// <param name="EquippedBadges">Badges in a slot, which is a subset of the total.</param>
/// <param name="ActivatedEffects">Effects switched on at least once; <paramref name="SelectedEffects"/>
/// is the smaller set worn right now.</param>
public sealed record PlayerRewardTotals(
    int TotalBadges,
    int EquippedBadges,
    int PlayersWithBadges,
    int DistinctBadgeCodes,
    int TotalEffects,
    int ActivatedEffects,
    int SelectedEffects,
    int WardrobeOutfits,
    int WardrobeUsers
);
