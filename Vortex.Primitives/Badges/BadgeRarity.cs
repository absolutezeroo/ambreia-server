namespace Vortex.Primitives.Badges;

/// <summary>Owner count to <see cref="BadgeRarityTier"/>, by the limits an operator set.</summary>
public static class BadgeRarity
{
    /// <summary>
    /// The tier a badge held by <paramref name="ownerCount"/> of <paramref name="totalPlayers"/>
    /// earns. The rarest tier whose limit the badge is at or under wins.
    /// </summary>
    /// <remarks>
    /// A badge nobody owns is <see cref="BadgeRarityTier.Common"/>, not unique: a code with zero
    /// holders is one that has never been granted, and calling that the rarest thing in the hotel
    /// would put every unused code above the ones players actually earned.
    /// </remarks>
    public static BadgeRarityTier Calculate(
        int ownerCount,
        int totalPlayers,
        BadgeRarityThresholds thresholds
    )
    {
        if (ownerCount <= 0 || totalPlayers < thresholds.MinimumPlayers)
        {
            return BadgeRarityTier.Common;
        }

        if (ownerCount <= thresholds.UniqueMaxOwners)
        {
            return BadgeRarityTier.Unique;
        }

        double share = (double)ownerCount / totalPlayers;

        return share switch
        {
            _ when share <= thresholds.LegendaryMaxShare => BadgeRarityTier.Legendary,
            _ when share <= thresholds.MythicalMaxShare => BadgeRarityTier.Mythical,
            _ when share <= thresholds.EpicMaxShare => BadgeRarityTier.Epic,
            _ when share <= thresholds.RareMaxShare => BadgeRarityTier.Rare,
            _ when share <= thresholds.UncommonMaxShare => BadgeRarityTier.Uncommon,
            _ => BadgeRarityTier.Common,
        };
    }
}
