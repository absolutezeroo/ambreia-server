namespace Vortex.Primitives.Badges;

/// <summary>
/// The badge rarity tiers, numbered exactly as the client numbers them.
/// </summary>
/// <remarks>
/// Ground truth is the client's own enum, <c>communication/enum/_SafeCls_3609.as</c>: it declares
/// <c>COMMON = 0</c>, an unnamed 1, <c>RARE = 2</c>, <c>VERY_RARE = 3</c>, <c>MYTHICAL = 4</c>, then
/// an unnamed 5 and 6. The names for the four it does not name come from the localisation keys the
/// same class maps each id to — <c>badge.rarity.uncommon</c>, <c>.epic</c>, <c>.legendary</c>,
/// <c>.unique</c> — which is also why <see cref="Epic"/> is called that and not "very rare".
/// <para>
/// Two client rules matter to anything that assigns a tier. <c>isRareOrHigher(id)</c> is
/// <c>id &gt;= 2</c>, and only a tier that passes <c>isStandaloneTier</c> gets its own label, glow
/// and leaderboard board; everything below is drawn as common. <see cref="Uncommon"/> passes only
/// while the client config flag <c>badge_rarity.uncommon</c> is on, and that flag is an external
/// variable this hotel does not currently serve — so a badge sent as <see cref="Uncommon"/> renders
/// exactly like <see cref="Common"/> until it is added.
/// </para>
/// <para>
/// The server decides the tier. The client never computes one: it draws whatever arrives as the
/// fourth field of a badge, so an unset value is not "no opinion", it is the claim that every badge
/// in the hotel is common.
/// </para>
/// </remarks>
public enum BadgeRarityTier
{
    Common = 0,

    /// <summary>Drawn as <see cref="Common"/> unless <c>badge_rarity.uncommon</c> is served.</summary>
    Uncommon = 1,

    Rare = 2,

    /// <summary>The client's <c>VERY_RARE</c>; its localisation key is <c>badge.rarity.epic</c>.</summary>
    Epic = 3,

    Mythical = 4,

    Legendary = 5,

    Unique = 6,
}
