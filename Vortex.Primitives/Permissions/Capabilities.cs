using System.Collections.Generic;

namespace Vortex.Primitives.Permissions;

/// <summary>
/// The canonical, code-declared set of authorization capabilities. Capabilities are fine-grained,
/// namespaced strings — the unit of authorization. Roles (in
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

    /// <summary>
    /// Running the server itself, from its console: reloading what it caches, and the graceful quit.
    /// Held apart from each other because ending the session is felt by every player.
    /// </summary>
    public static class Server
    {
        /// <summary>Reload plugins and reference caches (<c>reload</c>, <c>reload-plugin(s)</c>).</summary>
        public const string ConfigManage = "server.config.manage";

        /// <summary>Stop or restart the emulator (the graceful <c>quit</c> command).</summary>
        public const string Control = "server.control";

        public static IReadOnlyList<string> All { get; } = [ConfigManage, Control];
    }

    /// <summary>Building the world with the map editor (<c>/api/editor</c>).</summary>
    public static class World
    {
        /// <summary>Read, validate and save zone drafts; take a zone's edit lock.</summary>
        public const string ZoneEdit = "world.zone.edit";

        /// <summary>Publish a version (players get it), roll back, force someone else's lock.</summary>
        public const string ZonePublish = "world.zone.publish";

        public static IReadOnlyList<string> All { get; } = [ZoneEdit, ZonePublish];
    }

    /// <summary>Acting on characters as the server would (<c>/api/admin/characters</c>).</summary>
    public static class Rpg
    {
        /// <summary>Grant XP to a character: the test command of spec Personnage §16.</summary>
        public const string GrantXp = "rpg.character.grant_xp";

        public static IReadOnlyList<string> All { get; } = [GrantXp];
    }

    /// <summary>The RPG content pipeline (<c>/api/content</c>, spec Contenu §6).</summary>
    public static class Content
    {
        /// <summary>Import a whole content pack: what every player's classes and items read.</summary>
        public const string Publish = "content.publish";

        public static IReadOnlyList<string> All { get; } = [Publish];
    }

    /// <summary>Every declared capability, for validation.</summary>
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
        .. Server.All,
        .. World.All,
        .. Content.All,
        .. Rpg.All,
    ];
}
