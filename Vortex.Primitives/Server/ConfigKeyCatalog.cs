using System.Collections.Immutable;
using Vortex.Primitives.Moderation.WordFilter;

namespace Vortex.Primitives.Server;

/// <summary>The value shape of a runtime config key — drives dashboard input rendering and write-side validation.</summary>
public enum ConfigValueKind
{
    Int,
    Bool,
    String,
    Json,
}

/// <summary>
/// Static metadata for a known runtime config key: its default, value kind, human description and UI
/// grouping. The <c>IServerConfigGrain</c> only knows keys that already exist in the database, so this
/// catalog is what tells the dashboard which keys exist, what they mean, and what a sane default is
/// before an operator has ever overridden them.
/// </summary>
public sealed record ConfigKeyDescriptor(
    string Key,
    string DefaultValue,
    ConfigValueKind Kind,
    string Description,
    string Group
);

/// <summary>
/// The canonical, code-declared catalog of admin-editable runtime config keys. Kept as string literals
/// on purpose: this project can't reference the downstream feature-specific <c>*Config</c> constant
/// classes, so the keys/defaults are duplicated here as the single source of truth for the dashboard's
/// server-config editor.
/// </summary>
public static class ConfigKeyCatalog
{
    public static readonly ImmutableArray<ConfigKeyDescriptor> All =
    [
        new(
            "motd.lines",
            "[\"Welcome to Vortex! Have fun and be nice to each other.\"]",
            ConfigValueKind.Json,
            "Message-of-the-day lines shown on login (JSON array of strings)",
            "General"
        ),
        new(
            "badge.rarity.minimum_players",
            "100",
            ConfigValueKind.Int,
            "Registered players the hotel needs before badge rarity means anything. Under this, every badge is drawn as common — a hotel with eleven accounts would otherwise call its welcome badge legendary.",
            "Badges"
        ),
        new(
            "badge.rarity.unique_max_owners",
            "1",
            ConfigValueKind.Int,
            "A badge this many players or fewer hold is unique, whatever the hotel's size. The only tier set by an absolute count rather than a share.",
            "Badges"
        ),
        new(
            "badge.rarity.legendary_max_share",
            "0.001",
            ConfigValueKind.String,
            "Share of registered players who may hold a badge for it to be legendary (0.001 = 0.1%).",
            "Badges"
        ),
        new(
            "badge.rarity.mythical_max_share",
            "0.005",
            ConfigValueKind.String,
            "Share of registered players who may hold a badge for it to be mythical (0.005 = 0.5%).",
            "Badges"
        ),
        new(
            "badge.rarity.epic_max_share",
            "0.02",
            ConfigValueKind.String,
            "Share of registered players who may hold a badge for it to be epic (0.02 = 2%).",
            "Badges"
        ),
        new(
            "badge.rarity.rare_max_share",
            "0.08",
            ConfigValueKind.String,
            "Share of registered players who may hold a badge for it to be rare (0.08 = 8%). Rare is the lowest tier the client always draws on its own; anything below is drawn as common.",
            "Badges"
        ),
        new(
            "badge.rarity.uncommon_max_share",
            "0.25",
            ConfigValueKind.String,
            "Share of registered players who may hold a badge for it to be uncommon (0.25 = 25%). Changes nothing a player sees until the client is served 'badge_rarity.uncommon' in its external variables — without that flag the client draws this tier as common.",
            "Badges"
        ),
        new(
            "badge.rarity.refresh_seconds",
            "300",
            ConfigValueKind.Int,
            "How often badge owner counts are recounted from the database. Grants and revokes adjust the counts in between, so this only corrects drift.",
            "Badges"
        ),
        new(
            "respect.daily_limit",
            "3",
            ConfigValueKind.Int,
            "Respects a player may give per day. The client draws the respect button only while this budget is above zero, so a zero here hides the feature.",
            "Respect"
        ),
        new(
            "respect.pet_daily_limit",
            "3",
            ConfigValueKind.Int,
            "Pets a player may scratch per day, and the only limit on scratching. It is the number the client is handed at login and counts down from on its own, so a second rule of ours would make that count a lie.",
            "Respect"
        ),
        new(
            "respect.daily_replenishes",
            "0",
            ConfigValueKind.Int,
            "How many times a day a player may refill an exhausted respect budget. Zero (the default) hides the refill button.",
            "Respect"
        ),
        new(
            "friendlist.fragment_size",
            "500",
            ConfigValueKind.Int,
            "Friends per friend-list fragment",
            "Friend list"
        ),
        new(
            "friendlist.user_limit",
            "300",
            ConfigValueKind.Int,
            "Max friends for a normal user",
            "Friend list"
        ),
        new(
            "friendlist.normal_limit",
            "300",
            ConfigValueKind.Int,
            "Friend cap (normal)",
            "Friend list"
        ),
        new(
            "friendlist.extended_limit",
            "2000",
            ConfigValueKind.Int,
            "Friend cap (extended)",
            "Friend list"
        ),
        new(
            "friendlist.search_limit",
            "30",
            ConfigValueKind.Int,
            "Max results per friend search",
            "Friend list"
        ),
        new(
            "moderation.modtool_default_mute_minutes",
            "60",
            ConfigValueKind.Int,
            "Default mute duration for the CFH mod tool",
            "Moderation"
        ),
        new(
            "moderation.room_chatlog_limit",
            "100",
            ConfigValueKind.Int,
            "Max chat lines per room chatlog",
            "Moderation"
        ),
        new(
            "moderation.user_chatlog_room_limit",
            "10",
            ConfigValueKind.Int,
            "Max rooms in a user chatlog",
            "Moderation"
        ),
        new(
            "moderation.user_chatlog_messages_per_room",
            "50",
            ConfigValueKind.Int,
            "Max chat lines per room in a user chatlog",
            "Moderation"
        ),
        new(
            "group.members_per_page",
            "14",
            ConfigValueKind.Int,
            "Group members per page",
            "Groups"
        ),
        new(
            "group.creation_cost_credits",
            "10",
            ConfigValueKind.Int,
            "Credits to create a group",
            "Groups"
        ),
        new(
            "group.max_forum_page_size",
            "50",
            ConfigValueKind.Int,
            "Max forum page size",
            "Groups"
        ),
        new(
            "group.default_forum_page_size",
            "20",
            ConfigValueKind.Int,
            "Default forum page size",
            "Groups"
        ),
        new("group.max_name_length", "50", ConfigValueKind.Int, "Max group name length", "Groups"),
        new(
            "messenger.max_friends",
            "300",
            ConfigValueKind.Int,
            "Messenger friend cap",
            "Messenger"
        ),
        new(
            "messenger.message_history_limit",
            "50",
            ConfigValueKind.Int,
            "Offline message history limit",
            "Messenger"
        ),
        new(
            "club.gift_cycle_days",
            "31",
            ConfigValueKind.Int,
            "Days per Habbo Club gift/payday cycle",
            "Habbo Club"
        ),
        new(
            "club.streak_grace_days",
            "7",
            ConfigValueKind.Int,
            "Grace days to keep the club streak after expiry",
            "Habbo Club"
        ),
        new(
            "club.kickback_percent",
            "10",
            ConfigValueKind.Int,
            "Percent of spend returned as club kickback",
            "Habbo Club"
        ),
        new(
            "club.badge_code",
            "HC1",
            ConfigValueKind.String,
            "Badge granted on first club purchase",
            "Habbo Club"
        ),
        new(
            "club.vip_badge_code",
            "HC2",
            ConfigValueKind.String,
            "Badge granted while VIP",
            "Habbo Club"
        ),
        // Earnings window routing. One switch per category with a live source — mirrors
        // Vortex.Players EarningsConfig / EarningsRoutingPolicy (duplicated here because
        // Primitives can't reference that feature project).
        new(
            "earnings.achievements.to_vault",
            "true",
            ConfigValueKind.Bool,
            "Credits and duckets from achievement levels wait in the Earnings window until the player claims them. Off: they land in the wallet immediately. Other currencies are always paid directly. Earnings already waiting stay claimable when this changes.",
            "Earnings"
        ),
        new(
            "earnings.habbo_club.to_vault",
            "true",
            ConfigValueKind.Bool,
            "The Habbo Club payday waits in the Earnings window until the player claims it. Off: it lands in the wallet on payday. Paydays already waiting stay claimable when this changes.",
            "Earnings"
        ),
        new(
            "rooms.max_rooms_per_player",
            "50",
            ConfigValueKind.Int,
            "Max rooms a player may own",
            "Rooms"
        ),
        new(
            "room.filter.max_words",
            "50",
            ConfigValueKind.Int,
            "Words one room's own chat filter may hold. Lowering it does not drop words already saved; it only refuses the next one.",
            "Rooms"
        ),
        new(
            "room.filter.word_max_length",
            "64",
            ConfigValueKind.Int,
            "Longest word a room owner may add to their filter. The column holds 64, so a larger value here is refused by the database rather than honoured.",
            "Rooms"
        ),
        new(
            "room.filter.replacement",
            "bobba",
            ConfigValueKind.String,
            "What a filtered word is replaced with in the line everyone sees. Read when a room loads its filter and when the filter is edited, not per chat line, so a change reaches an already-awake room on its next edit or reload.",
            "Rooms"
        ),
        // The hotel-wide word filter. Its constants live beside it in this project, so unlike the
        // keys above they are named rather than repeated as literals.
        new(
            WordFilterConfig.ReplacementKey,
            WordFilterConfig.ReplacementDefault,
            ConfigValueKind.String,
            "What a word on the hotel-wide filter is replaced with, when that word's action is Replace and it names no replacement of its own. Read when the list compiles; editing any word reloads it.",
            "Moderation"
        ),
        new(
            WordFilterConfig.RefusedMessageKey,
            WordFilterConfig.RefusedMessageDefault,
            ConfigValueKind.String,
            "The bubble a player sees when the hotel-wide filter refuses what they wrote. Read when the list compiles; editing any word reloads it.",
            "Moderation"
        ),
        // Raid protection. A room owner picks low/medium/high in their own panel; what each of the
        // three means in arrivals per minute is the hotel's call, and it has to be, because the
        // right number depends on how busy the hotel is. Habbo publishes no threshold — the client
        // never receives one — so unlike most keys here these defaults are ours, not a mirror of
        // anything official. Read together in one round trip by RoomRaidProtectionSystem.
        new(
            "room.raid_protection.threshold.low",
            "12",
            ConfigValueKind.Int,
            "Arrivals in one minute that count as a raid on the 'low' setting. Too low and a popular room fights its own visitors.",
            "Raid protection"
        ),
        new(
            "room.raid_protection.threshold.medium",
            "8",
            ConfigValueKind.Int,
            "Arrivals in one minute that count as a raid on the 'medium' setting.",
            "Raid protection"
        ),
        new(
            "room.raid_protection.threshold.high",
            "5",
            ConfigValueKind.Int,
            "Arrivals in one minute that count as a raid on the 'high' setting. Also the usual choice for the post-incident guard period.",
            "Raid protection"
        ),
        // Navigator result caps. Keys/defaults mirror Vortex.Primitives NavigatorConfig.
        new(
            "navigator.search_result_limit",
            "50",
            ConfigValueKind.Int,
            "Rooms returned by one navigator search",
            "Navigator"
        ),
        new(
            "navigator.category_result_limit",
            "20",
            ConfigValueKind.Int,
            "Rooms returned per category block",
            "Navigator"
        ),
        new(
            "navigator.history_limit",
            "25",
            ConfigValueKind.Int,
            "Rooms kept in the visited/most-visited lists",
            "Navigator"
        ),
        new(
            "navigator.favourite_limit",
            "30",
            ConfigValueKind.Int,
            "Rooms a player may favourite (the client refuses to add past this, so 0 disables favourites entirely)",
            "Navigator"
        ),
    ];

    /// <summary>The descriptor for <paramref name="key"/>, or null if the key is not a known config key.</summary>
    public static ConfigKeyDescriptor? Find(string key)
    {
        foreach (ConfigKeyDescriptor descriptor in All)
        {
            if (descriptor.Key == key)
            {
                return descriptor;
            }
        }

        return null;
    }
}
