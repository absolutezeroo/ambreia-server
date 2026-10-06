using System.Collections.Generic;
using Vortex.Primitives.Permissions;

namespace Vortex.Authentication.Permissions;

/// <summary>
/// Default role bootstrap. Seeded only into a fresh database (see <see cref="PermissionSeederService"/>);
/// once a role exists its capabilities are administrator-managed and no longer overwritten.
/// </summary>
internal static class DefaultRoles
{
    public const string OwnerKey = "owner";

    public sealed record RoleSeed(string Key, string Name, IReadOnlyList<string> Capabilities);

    public static readonly IReadOnlyList<RoleSeed> All =
    [
        new RoleSeed("player", "Player", []),
        new RoleSeed(
            "moderator",
            "Moderator",
            [
                Capabilities.Moderation.Kick,
                Capabilities.Moderation.Mute,
                Capabilities.Moderation.Alert,
                Capabilities.Moderation.TradingLock,
                Capabilities.Moderation.Chatlogs,
                Capabilities.Moderation.Cfh,
                Capabilities.Room.ModerateAny,
                Capabilities.Navigator.StaffPick,
                Capabilities.Dashboard.OverviewRead,
                Capabilities.Dashboard.AuditRead,
                Capabilities.Dashboard.PlayersRead,
                Capabilities.Dashboard.OpsKickPlayer,
                // The same authority over the same players' words as Mute above, one surface
                // further out: the website's feed is public, and a moderator who can silence a
                // room has no reason to need a second person to take a post down.
                Capabilities.Dashboard.OpsFeedModerate,
                // Same reasoning again: the word list decides what every player may write, which
                // is the moderator's remit, and the journal is the record of who tried.
                Capabilities.Dashboard.OpsWordFilterManage,
            ]
        ),
        new RoleSeed(
            "economy",
            "Economy staff",
            [
                Capabilities.Economy.GrantCredits,
                Capabilities.Economy.GrantActivityPoints,
                Capabilities.Economy.GrantItem,
                Capabilities.Dashboard.OverviewRead,
                Capabilities.Dashboard.AuditRead,
                Capabilities.Dashboard.EconomyRead,
                Capabilities.Dashboard.PlayersRead,
                Capabilities.Dashboard.FurnitureRead,
                Capabilities.Dashboard.OpsGrantCurrency,
                Capabilities.Dashboard.OpsGrantItem,
            ]
        ),
        new RoleSeed(
            "admin",
            "Administrator",
            [
                Capabilities.Room.BuildAny,
                Capabilities.Room.ModerateAny,
                Capabilities.Room.FurniEdit,
                Capabilities.Furniture.DefinitionEdit,
                Capabilities.Navigator.StaffPick,
                Capabilities.Moderation.Kick,
                Capabilities.Moderation.Mute,
                Capabilities.Moderation.Alert,
                Capabilities.Moderation.Ban,
                Capabilities.Moderation.TradingLock,
                Capabilities.Moderation.Chatlogs,
                Capabilities.Moderation.Cfh,
                Capabilities.Economy.GrantCredits,
                Capabilities.Economy.GrantActivityPoints,
                Capabilities.Economy.GrantItem,
                Capabilities.Dashboard.OverviewRead,
                Capabilities.Dashboard.AuditRead,
                Capabilities.Dashboard.EconomyRead,
                Capabilities.Dashboard.PlayersRead,
                Capabilities.Dashboard.FurnitureRead,
                Capabilities.Dashboard.OpsGrantCurrency,
                Capabilities.Dashboard.OpsGrantItem,
                Capabilities.Dashboard.OpsKickPlayer,
            ]
        ),
        new RoleSeed("owner", "Owner", [Capabilities.Wildcard]),
    ];
}
