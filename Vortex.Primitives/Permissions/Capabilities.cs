using System.Collections.Generic;

namespace Vortex.Primitives.Permissions;

/// <summary>
/// The canonical, code-declared set of authorization capabilities. Capabilities are fine-grained,
/// namespaced strings — the unit of authorization across both the game and the dashboard. Roles (in
/// the database) map to sets of these; an account's effective rights are the union of its roles'
/// capabilities. New features declare their own capability here so the set stays discoverable and
/// typo-safe instead of being scattered rank checks.
/// </summary>
public static class Capabilities
{
    /// <summary>Grants every capability. Reserved for the owner role.</summary>
    public const string Wildcard = "*";

    public static class Room
    {
        /// <summary>Build/decorate in any room, bypassing ownership and room-rights.</summary>
        public const string BuildAny = "room.build.any";

        /// <summary>Moderate (kick/mute/settings) in any room.</summary>
        public const string ModerateAny = "room.moderate.any";

        /// <summary>Rewrite a placed furni's stored row through the in-client furni editor:
        /// transform, extra data, owner and definition. Deliberately separate from
        /// <see cref="BuildAny"/> — that only lifts the ownership/room-rights check on the ordinary
        /// place/move/pickup path, and every edit it allows is one an ordinary player could also
        /// perform in their own room. This one grants writes with no player-facing equivalent
        /// (reassigning ownership, swapping the definition out from under an item, setting an
        /// arbitrary altitude), so it is granted separately.</summary>
        public const string FurniEdit = "room.furni.edit";
    }

    public static class Furniture
    {
        /// <summary>Edit rows in <c>furniture_definitions</c> from inside the game client: the
        /// interaction (logic), footprint, stack height, walk/sit/lay flags, state count and trade
        /// policy of a furniture *type*. Hotel-wide by nature — one edit changes every placed copy
        /// and every future purchase — which is why it is not folded into
        /// <see cref="Room.FurniEdit"/>, whose blast radius is a single placed item.</summary>
        public const string DefinitionEdit = "furniture.definition.edit";
    }

    public static class Moderation
    {
        public const string Kick = "moderation.kick";
        public const string Mute = "moderation.mute";
        public const string Alert = "moderation.alert";
        public const string Ban = "moderation.ban";
        public const string TradingLock = "moderation.trading_lock";

        /// <summary>Matches the WIN63 client's own distinct "chatlogsPermission" tool flag — reading
        /// a player's chat history is gated separately from being able to sanction them.</summary>
        public const string Chatlogs = "moderation.chatlogs";

        /// <summary>Matches the WIN63 client's own distinct "cfhPermission" tool flag — handling CFH
        /// tickets (pick/close/release/default-action) is gated separately from direct
        /// kick/mute/ban/alert actions.</summary>
        public const string Cfh = "moderation.cfh";
    }

    public static class Economy
    {
        public const string GrantCredits = "economy.credits.grant";
        public const string GrantActivityPoints = "economy.activitypoints.grant";
        public const string GrantItem = "economy.item.grant";
    }

    public static class Dashboard
    {
        public const string OverviewRead = "dashboard.overview.read";
        public const string AuditRead = "dashboard.audit.read";
        public const string EconomyRead = "dashboard.economy.read";
        public const string PlayersRead = "dashboard.players.read";
        public const string FurnitureRead = "dashboard.furniture.read";

        public const string OpsGrantCurrency = "dashboard.ops.currency.grant";
        public const string OpsGrantItem = "dashboard.ops.item.grant";
        public const string OpsKickPlayer = "dashboard.ops.player.kick";
        public const string OpsBanAccount = "dashboard.ops.player.ban";
        public const string OpsMutePlayer = "dashboard.ops.player.mute";
        public const string OpsTradingLock = "dashboard.ops.player.trading_lock";

        /// <summary>
        /// Erasing a player's personal content from the forensic tables on request. Granted on its
        /// own and to almost nobody: it is the only operator action that destroys evidence rather
        /// than producing it, and the sanctions above are no reason at all to hold it.
        /// </summary>
        public const string OpsForensicsPurge = "dashboard.ops.player.forensics_purge";
        public const string OpsCfhManage = "dashboard.ops.cfh.manage";
        public const string OpsRoomsManage = "dashboard.ops.rooms.manage";
        public const string CatalogRead = "dashboard.catalog.read";
        public const string OpsCatalogManage = "dashboard.ops.catalog.manage";
        public const string OpsFurnitureManage = "dashboard.ops.furniture.manage";
        public const string GroupsRead = "dashboard.groups.read";

        /// <summary>
        /// Acting on a guild from outside it: hiding or deleting forum content, answering a
        /// membership request, lifting a guild ban, removing a member, disbanding the guild.
        /// </summary>
        /// <remarks>
        /// One capability for the guild and its forum because it is one job: the reason an operator
        /// is in a guild's forum is almost always the reason they are about to remove someone from
        /// it. Splitting it would mean an operator who can delete the post but not the poster.
        /// </remarks>
        public const string OpsGuildsManage = "dashboard.ops.guilds.manage";

        /// <summary>
        /// Keeping the hotel-wide word list — adding, editing and removing words — and reading the
        /// journal of what it caught.
        /// </summary>
        /// <remarks>
        /// One capability for both, because the journal is how an operator decides what the list
        /// should hold: the reason to read it is almost always the edit that follows.
        /// </remarks>
        public const string OpsWordFilterManage = "dashboard.ops.wordfilter.manage";
        public const string PetsRead = "dashboard.pets.read";
        public const string CfhRead = "dashboard.cfh.read";

        /// <summary>
        /// Searching what players said to each other. Deliberately not folded into
        /// <see cref="AuditRead" /> or the room and player pages that already show chat in context:
        /// this one answers "who said this word, anywhere", which is the only chat read that does not
        /// start from an incident an operator is already investigating. It is the most privacy-bearing
        /// surface the dashboard has, so it is granted on its own or not at all.
        /// </summary>
        public const string ChatlogsRead = "dashboard.chatlogs.read";
        public const string CatalogPurchasesRead = "dashboard.catalog.purchases.read";
        public const string QuestsRead = "dashboard.quests.read";
        public const string OpsQuestsManage = "dashboard.ops.quests.manage";

        /// <summary>The gift dialog's parcels, boxes and ribbons.</summary>
        public const string GiftWrappingsRead = "dashboard.gift_wrappings.read";
        public const string OpsGiftWrappingsManage = "dashboard.ops.gift_wrappings.manage";

        /// <summary>Fishing's four content tables, and what players have caught out of them.</summary>
        public const string FishingRead = "dashboard.fishing.read";
        public const string OpsFishingManage = "dashboard.ops.fishing.manage";

        public const string PrizePoolsRead = "dashboard.prize_pools.read";
        public const string OpsPrizePoolsManage = "dashboard.ops.prize_pools.manage";

        public const string ConfigRead = "dashboard.config.read";
        public const string OpsConfigManage = "dashboard.ops.config.manage";
        public const string PerformanceRead = "dashboard.performance.read";

        /// <summary>Take a database dump on demand and see the ones already kept. Separate from
        /// config management: a hotel may well want the safety net readable and triggerable by
        /// operators who are not allowed to change what the server runs on.</summary>
        public const string OpsDatabaseBackup = "dashboard.ops.database.backup";

        /// <summary>Achievement definitions, their level ladders and hotel-wide progression.</summary>
        public const string AchievementsRead = "dashboard.achievements.read";

        /// <summary>Room actors that are neither players nor furni: bots and hand items.</summary>
        public const string BotsRead = "dashboard.bots.read";

        /// <summary>The navigator's own configuration (top-level contexts, categories, quick
        /// links) — the rows that decide what the client's navigator left pane offers.</summary>
        public const string NavigatorRead = "dashboard.navigator.read";

        /// <summary>Edit that navigator configuration. Separate from <see cref="NavigatorRead"/>
        /// because a bad row is hotel-wide and immediately visible to every player.</summary>
        public const string OpsNavigatorManage = "dashboard.ops.navigator.manage";

        /// <summary>Social graph and guild forums: friendships, requests, private-message volume,
        /// thread/post activity.</summary>
        public const string SocialRead = "dashboard.social.read";

        /// <summary>The staff roster itself: roles, the capabilities each role grants, who holds
        /// them, and the sanction preset ladder.</summary>
        public const string StaffRead = "dashboard.staff.read";

        /// <summary>Edit the staff roster itself: roles, what they grant, and who holds them. Held
        /// apart from every other ops capability because it is the one that can grant capabilities —
        /// including itself.</summary>
        public const string OpsStaffManage = "dashboard.ops.staff.manage";

        /// <summary>Author the content the other read surfaces describe: achievement ladders, bots
        /// and hand items, player grants and the economy's smaller tables.</summary>
        public const string OpsContentManage = "dashboard.ops.content.manage";

        /// <summary>Read the website's news: articles and their translations, the categories they are
        /// filed under, and the languages the site publishes in.</summary>
        public const string ArticlesRead = "dashboard.articles.read";

        /// <summary>
        /// Write the website's news. Held apart from <see cref="OpsContentManage"/> on purpose: the
        /// person who writes the hotel's announcements is rarely the person who should be able to
        /// edit achievement ladders or grant currency, and one capability for both would force that.
        /// </summary>
        public const string OpsArticlesManage = "dashboard.ops.articles.manage";

        /// <summary>
        /// Edit the gamedata files the game client downloads — variables, texts, furnidata,
        /// productdata. One capability rather than the usual read/write pair: there is nothing to
        /// look at on that page except the raw client values, so anyone trusted to read them is the
        /// person who edits them. Held well apart from the rest: a bad save here is not a wrong row,
        /// it is a client that will not boot.
        /// </summary>
        public const string OpsGamedataManage = "dashboard.ops.gamedata.manage";

        /// <summary>
        /// Follow the emulator's live console — the same lines the terminal shows. Read-only, but the
        /// stream carries whatever the server logs, so it is at least as sensitive as the audit
        /// trail and is held apart from running commands.
        /// </summary>
        public const string ServerConsoleRead = "dashboard.server.console.read";

        /// <summary>
        /// Run an operator command from the dashboard console. Necessary but not sufficient: each
        /// command additionally declares the capability of whatever it acts on, so this grant alone
        /// only reaches the commands that gate on nothing else.
        /// </summary>
        public const string OpsServerConsole = "dashboard.ops.server.console";

        /// <summary>
        /// Change whether the emulator is running at all — the graceful <c>quit</c> command, and the
        /// supervisor's start/stop/restart. Every player on the hotel feels this one, so it is held
        /// apart from console access: following the logs is not permission to end the session.
        /// </summary>
        public const string OpsServerControl = "dashboard.ops.server.control";

        /// <summary>
        /// Every <c>dashboard.*</c> capability, declared once. This is the single source the whole
        /// dashboard reads from: <c>DashboardWebHost</c> registers one authorization policy per entry
        /// and <c>DashboardAuthService</c> gates login on holding at least one of them. Both used to
        /// carry their own hand-copied duplicate of this list, so a capability added to the constants
        /// above but missed in one of them compiled and tested green and then failed at runtime with
        /// <c>AuthorizationPolicy named '&lt;capability&gt;' was not found</c>. Adding the constant is
        /// now enough — and <c>CapabilityDeclarationTests</c> fails the build if a new constant is not
        /// listed here.
        /// </summary>
        public static IReadOnlyList<string> All { get; } =
        [
            OverviewRead,
            AuditRead,
            EconomyRead,
            PlayersRead,
            FurnitureRead,
            OpsGrantCurrency,
            OpsGrantItem,
            OpsKickPlayer,
            OpsBanAccount,
            OpsMutePlayer,
            OpsTradingLock,
            OpsForensicsPurge,
            OpsCfhManage,
            OpsRoomsManage,
            CatalogRead,
            OpsCatalogManage,
            OpsFurnitureManage,
            GroupsRead,
            OpsGuildsManage,
            OpsWordFilterManage,
            PetsRead,
            CfhRead,
            ChatlogsRead,
            CatalogPurchasesRead,
            QuestsRead,
            OpsQuestsManage,
            GiftWrappingsRead,
            OpsGiftWrappingsManage,
            FishingRead,
            OpsFishingManage,
            PrizePoolsRead,
            OpsPrizePoolsManage,
            ConfigRead,
            OpsConfigManage,
            PerformanceRead,
            OpsDatabaseBackup,
            AchievementsRead,
            BotsRead,
            NavigatorRead,
            OpsNavigatorManage,
            SocialRead,
            StaffRead,
            OpsStaffManage,
            OpsContentManage,
            ArticlesRead,
            OpsArticlesManage,
            OpsGamedataManage,
            ServerConsoleRead,
            OpsServerConsole,
            OpsServerControl,
        ];
    }

    /// <summary>Every declared capability, for validation and dashboard enumeration.</summary>
    public static IReadOnlyCollection<string> All { get; } =
    [
        Wildcard,
        Room.BuildAny,
        Room.ModerateAny,
        Room.FurniEdit,
        Furniture.DefinitionEdit,
        Moderation.Kick,
        Moderation.Mute,
        Moderation.Alert,
        Moderation.Ban,
        Moderation.TradingLock,
        Moderation.Chatlogs,
        Moderation.Cfh,
        Economy.GrantCredits,
        Economy.GrantActivityPoints,
        Economy.GrantItem,
        .. Dashboard.All,
    ];
}
