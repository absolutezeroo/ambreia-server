namespace Vortex.Primitives.Badges;

/// <summary>The three boards the client's badge leaderboard dropdown offers.</summary>
/// <remarks>
/// The ids are the client's own, declared in <c>groups/_SafeCls_2379.as</c> as
/// <c>TOTAL_BADGES = 0</c>, <c>BADGES_BY_RARITY = 1</c> and <c>ACHIEVEMENT_LEVEL = 2</c>. Only
/// <see cref="ByRarity"/> reads the rarity that travels beside it; the other two ignore it, and the
/// client sends its <c>DEFAULT_RARITY</c> of -1 for them.
/// </remarks>
public enum BadgeLeaderboardType
{
    TotalBadges = 0,
    ByRarity = 1,
    AchievementLevel = 2,
}
