using System.Collections.Immutable;

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
            "benchmark.enabled",
            "false",
            ConfigValueKind.Bool,
            "Allow the dashboard to run load tests against this hotel. Off by default: a run opens hundreds of real connections and competes with live players for the same room ticks.",
            "Benchmark"
        ),
        new(
            "motd.lines",
            "[\"Welcome to Vortex! Have fun and be nice to each other.\"]",
            ConfigValueKind.Json,
            "Message-of-the-day lines shown on login (JSON array of strings)",
            "General"
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
            "Pets a player may scratch per day. Applies on top of the per-pet daily cap in the room config.",
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
        new(
            "rooms.max_rooms_per_player",
            "50",
            ConfigValueKind.Int,
            "Max rooms a player may own",
            "Rooms"
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
        // Freeze minigame balance. Keys/defaults mirror Vortex.Rooms FreezeConfig / FreezeSettings
        // (duplicated here because Primitives can't reference that feature project).
        new("freeze.start_lives", "3", ConfigValueKind.Int, "Freeze: starting lives", "Freeze"),
        new(
            "freeze.max_lives",
            "5",
            ConfigValueKind.Int,
            "Freeze: max lives (extra-life cap)",
            "Freeze"
        ),
        new(
            "freeze.start_snowballs",
            "1",
            ConfigValueKind.Int,
            "Freeze: starting snowballs",
            "Freeze"
        ),
        new(
            "freeze.max_snowballs",
            "5",
            ConfigValueKind.Int,
            "Freeze: max carried snowballs",
            "Freeze"
        ),
        new(
            "freeze.snowball_regen_ticks",
            "2",
            ConfigValueKind.Int,
            "Freeze: game ticks (1s each) to regenerate one snowball (0 disables)",
            "Freeze"
        ),
        new(
            "freeze.max_explosion_boost",
            "5",
            ConfigValueKind.Int,
            "Freeze: max blast radius boost",
            "Freeze"
        ),
        new(
            "freeze.frozen_ticks",
            "5",
            ConfigValueKind.Int,
            "Freeze: frozen duration in game ticks (1s each)",
            "Freeze"
        ),
        new(
            "freeze.protection_ticks",
            "12",
            ConfigValueKind.Int,
            "Freeze: shield duration in game ticks (1s each)",
            "Freeze"
        ),
        new(
            "freeze.freeze_lose_snowballs",
            "1",
            ConfigValueKind.Int,
            "Freeze: snowballs lost when frozen",
            "Freeze"
        ),
        new(
            "freeze.freeze_lose_boost",
            "1",
            ConfigValueKind.Int,
            "Freeze: blast boost lost when frozen",
            "Freeze"
        ),
        new(
            "freeze.powerup_chance_percent",
            "25",
            ConfigValueKind.Int,
            "Freeze: chance (%) a destroyed block yields a power-up",
            "Freeze"
        ),
        new(
            "freeze.protection_stacks",
            "false",
            ConfigValueKind.Bool,
            "Freeze: shields stack in duration instead of refreshing",
            "Freeze"
        ),
        new(
            "freeze.points_freeze_player",
            "10",
            ConfigValueKind.Int,
            "Freeze: points for freezing an enemy",
            "Freeze"
        ),
        new(
            "freeze.points_destroy_block",
            "5",
            ConfigValueKind.Int,
            "Freeze: points for destroying a block",
            "Freeze"
        ),
        new(
            "freeze.points_powerup",
            "5",
            ConfigValueKind.Int,
            "Freeze: points for collecting a power-up",
            "Freeze"
        ),
        new(
            "freeze.max_players_per_team",
            "5",
            ConfigValueKind.Int,
            "Freeze: max players per team",
            "Freeze"
        ),
        // Battle Banzai balance. Keys/defaults mirror Vortex.Rooms BanzaiConfig / BanzaiSettings
        // (duplicated here because Primitives can't reference that feature project).
        new(
            "banzai.points_lock_tile",
            "1",
            ConfigValueKind.Int,
            "Banzai: points per locked tile (stepped and enclosed)",
            "Banzai"
        ),
        new(
            "banzai.points_fill_tile",
            "0",
            ConfigValueKind.Int,
            "Banzai: points for advancing your own claim",
            "Banzai"
        ),
        new(
            "banzai.points_hijack_tile",
            "0",
            ConfigValueKind.Int,
            "Banzai: points for stealing a neutral/enemy tile",
            "Banzai"
        ),
        new(
            "banzai.max_players_per_team",
            "5",
            ConfigValueKind.Int,
            "Banzai: max players per team",
            "Banzai"
        ),
        new(
            "banzai.lock_batch_per_tick",
            "32",
            ConfigValueKind.Int,
            "Banzai: enclosed-region tiles painted per room tick (protects the wired event queue)",
            "Banzai"
        ),
        // Football balance. Keys/defaults mirror Vortex.Rooms FootballConfig / FootballSettings.
        // Habbo's own kick numbers are not authoritatively known — no capture, and the client's
        // pushable logic carries no rules — so these defaults come from the reference emulators,
        // which is evidence rather than authority. All editable, precisely for that reason.
        new(
            "football.kick_distance",
            "5",
            ConfigValueKind.Int,
            "Football: tiles a deliberate kick carries the ball",
            "Football"
        ),
        new(
            "football.drag_distance",
            "1",
            ConfigValueKind.Int,
            "Football: tiles the ball moves when a player walks through it rather than at it",
            "Football"
        ),
        new(
            "football.tackle_distance",
            "4",
            ConfigValueKind.Int,
            "Football: tiles the ball moves when clicked from an adjacent tile",
            "Football"
        ),
        new(
            "football.top_pace",
            "5",
            ConfigValueKind.Int,
            "Football: the pace a struck ball leaves at, 1-9; it slows one step per hop (5 = 100ms, 4 = 125ms ... 1 = 500ms)",
            "Football"
        ),
        new(
            "football.avatar_stop_chance_percent",
            "70",
            ConfigValueKind.Int,
            "Football: chance in percent that a player in the ball's path stops it",
            "Football"
        ),
        new(
            "football.goal_points",
            "1",
            ConfigValueKind.Int,
            "Football: points a goal awards the team whose colour the goal carries",
            "Football"
        ),
        new(
            "football.goal_reset_ms",
            "2000",
            ConfigValueKind.Int,
            "Football: how long the ball sits in the goal before returning to the kickoff spot (0 leaves it in the net)",
            "Football"
        ),
        new(
            "habbicons.recent_limit",
            "10",
            ConfigValueKind.Int,
            "How many recently-used Habbicons the client's quick row keeps. The client caps its own list at 10; sending more is harmless but never shown.",
            "Habbicons"
        ),
        new(
            "habbicons.use_cooldown_ms",
            "500",
            ConfigValueKind.Int,
            "Minimum gap between one player's Habbicon uses. Separate from chat flood control, which already gates the room half — this also covers private conversations.",
            "Habbicons"
        ),
        new(
            "reward_tracks.enabled",
            "true",
            ConfigValueKind.Bool,
            "Master switch. Off sends the client the feature's own disabled flag, which hides every track rather than showing an empty list.",
            "Reward tracks"
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
