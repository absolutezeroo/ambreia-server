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
        new(
            "camera.credit_price",
            "2",
            ConfigValueKind.Int,
            "Credits charged to take a photo. The client reads this once per room entry and keeps it for the visit, so an edit reaches a player on their next room change.",
            "Camera"
        ),
        new(
            "camera.ducket_price",
            "0",
            ConfigValueKind.Int,
            "Duckets charged to take a photo, alongside the credit price. Zero on the official hotel.",
            "Camera"
        ),
        new(
            "camera.publish_ducket_price",
            "10",
            ConfigValueKind.Int,
            "Duckets charged to publish a photo to the hotel's feed, on top of taking it.",
            "Camera"
        ),
        new(
            "camera.renders_per_hour",
            "30",
            ConfigValueKind.Int,
            "Photos one player may take per hour. The rate limit, not the price, is what stops a scripted client turning the renderer into the hotel's most expensive loop — it is checked before the payload is even decompressed.",
            "Camera"
        ),
        new(
            "camera.publish_cooldown_seconds",
            "1800",
            ConfigValueKind.Int,
            "Seconds between two publishes by the same player. The client turns a refusal into \"wait N minutes\" from this number.",
            "Camera"
        ),
        new(
            "camera.competition_entries_per_day",
            "3",
            ConfigValueKind.Int,
            "Competition entries per player per day. Past it the client is told 'too-many-submits', which it has its own wording for.",
            "Camera"
        ),
        new(
            "camera.external_image_prefixes",
            "",
            ConfigValueKind.String,
            "Semicolon-separated url prefixes a photo may fetch images from: badges, photos on a wall. A sprite's name can be a url the payload chose, so this list is what stops the hotel fetching a stranger's. Empty means none. Give the full prefix, path included.",
            "Camera"
        ),
        new(
            "camera.max_payload_bytes",
            "524288",
            ConfigValueKind.Int,
            "Largest compressed scene accepted from the client, refused before a byte is inflated. The client caps nothing itself.",
            "Camera"
        ),
        new(
            "camera.max_scene_bytes",
            "4194304",
            ConfigValueKind.Int,
            "Ceiling on the scene once decompressed, applied WHILE inflating. This is the compression-bomb guard: a small upload can expand to gigabytes, and measuring afterwards measures the damage.",
            "Camera"
        ),
        new(
            "camera.max_planes",
            "256",
            ConfigValueKind.Int,
            "Most floor and wall planes one photo may describe. Lower it if the renderer is costing more than a photo is worth.",
            "Camera"
        ),
        new(
            "camera.max_sprites",
            "4096",
            ConfigValueKind.Int,
            "Most sprites one photo may describe — furniture parts, avatar limbs, badges. A crowded room in a wide shot is the honest upper end.",
            "Camera"
        ),
        new(
            "camera.max_masks_per_plane",
            "64",
            ConfigValueKind.Int,
            "Most door and window cut-outs on a single plane.",
            "Camera"
        ),
        new(
            "camera.max_texture_columns_per_plane",
            "128",
            ConfigValueKind.Int,
            "Most texture columns painted onto a single plane.",
            "Camera"
        ),
        new(
            "camera.timestamp_skew_seconds",
            "900",
            ConfigValueKind.Int,
            "How far a photo's own timestamp may sit from server time. The client's seal carries no secret and authenticates nothing; a freshness window is the one thing it honestly buys, and it stops one scene being replayed all afternoon.",
            "Camera"
        ),
        new(
            "camera.url_path_prefix",
            "photos/",
            ConfigValueKind.String,
            "Path prefix of the url handed to the client, which prepends its own stories.image_url_base external variable. The two have to agree or every photo is fetched from somewhere that has never heard of this hotel.",
            "Camera"
        ),
        new(
            "camera.max_photo_dimension",
            "1600",
            ConfigValueKind.Int,
            "Ceiling on either side of a rendered photo. Nothing in the payload bounds a coordinate, so this is what stands between a corner at a billion and an allocation that fails.",
            "Camera"
        ),
        new(
            "camera.max_external_fetches",
            "30",
            ConfigValueKind.Int,
            "How many images one photo may fetch. The allowlist says where, this says how often, so one photo cannot become a burst against the asset host. Thirty is where the client stops too.",
            "Camera"
        ),
        new(
            "camera.external_timeout_seconds",
            "5",
            ConfigValueKind.Int,
            "How long one external image may take before the photo goes on without it. A host that stops answering must not become a render that never ends.",
            "Camera"
        ),
        new(
            "camera.max_external_bytes",
            "1048576",
            ConfigValueKind.Int,
            "Largest external image accepted, counted while reading rather than taken from the sender's Content-Length.",
            "Camera"
        ),
        new(
            "camera.photo_furni_classname",
            "external_image_wallitem_photo",
            ConfigValueKind.String,
            "Classname of the furniture a bought photo becomes. Nothing is charged if no definition carries this name.",
            "Camera"
        ),
        new(
            "camera.furniture_asset_url",
            "",
            ConfigValueKind.String,
            "Where furniture .nitro bundles are served, ending in a slash. Empty means photos show no furniture.",
            "Camera"
        ),
        new(
            "camera.avatar_asset_url",
            "",
            ConfigValueKind.String,
            "Where pet and avatar bundles are served (the client's gordon directory), ending in a slash.",
            "Camera"
        ),
        new(
            "camera.figuremap_url",
            "",
            ConfigValueKind.String,
            "The client's figuremap.xml. Without it an avatar's parts cannot be traced to their bundles.",
            "Camera"
        ),
        new(
            "camera.max_bundle_fetches",
            "64",
            ConfigValueKind.Int,
            "How many asset bundles one photo may fetch. Bounds the pet and avatar families, whose bundle name comes from the payload.",
            "Camera"
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
