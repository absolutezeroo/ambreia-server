using System.Collections.Immutable;

namespace Vortex.Progression.Configuration;

/// <summary>
/// Where each badge rarity tier ends, served live from <c>IServerConfigGrain</c> so an operator can
/// retune them without a deploy. Declared as key/default pairs in the same shape as
/// <c>RespectConfig</c>; the dashboard editor learns about them from <c>ConfigKeyCatalog</c>.
/// </summary>
/// <remarks>
/// These are hotel policy, not protocol. The client never decides a rarity — it draws whatever the
/// server sends — so how common a badge has to be for each tier is a decision that belongs to
/// whoever runs the hotel, and the defaults below are only a starting point.
/// </remarks>
public static class BadgeRarityConfig
{
    /// <summary>Below this many registered players every badge stays common.</summary>
    public const string MinimumPlayersKey = "badge.rarity.minimum_players";
    public const int MinimumPlayersDefault = 100;

    /// <summary>A badge this many players or fewer hold is unique, whatever the hotel's size.</summary>
    public const string UniqueMaxOwnersKey = "badge.rarity.unique_max_owners";
    public const int UniqueMaxOwnersDefault = 1;

    public const string LegendaryMaxShareKey = "badge.rarity.legendary_max_share";
    public const string LegendaryMaxShareDefault = "0.001";

    public const string MythicalMaxShareKey = "badge.rarity.mythical_max_share";
    public const string MythicalMaxShareDefault = "0.005";

    public const string EpicMaxShareKey = "badge.rarity.epic_max_share";
    public const string EpicMaxShareDefault = "0.02";

    public const string RareMaxShareKey = "badge.rarity.rare_max_share";
    public const string RareMaxShareDefault = "0.08";

    /// <summary>
    /// The uncommon tier is drawn as common unless the client is served
    /// <c>badge_rarity.uncommon</c> in its external variables, so this limit changes nothing a
    /// player sees until that flag is added.
    /// </summary>
    public const string UncommonMaxShareKey = "badge.rarity.uncommon_max_share";
    public const string UncommonMaxShareDefault = "0.25";

    /// <summary>How often owner counts are recounted; grants and revokes adjust them in between.</summary>
    public const string RefreshSecondsKey = "badge.rarity.refresh_seconds";
    public const int RefreshSecondsDefault = 300;

    public static readonly ImmutableArray<string> AllKeys =
    [
        MinimumPlayersKey,
        UniqueMaxOwnersKey,
        LegendaryMaxShareKey,
        MythicalMaxShareKey,
        EpicMaxShareKey,
        RareMaxShareKey,
        UncommonMaxShareKey,
        RefreshSecondsKey,
    ];
}
