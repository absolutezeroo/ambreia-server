using System.Collections.Generic;
using Vortex.Primitives.Permissions;

namespace Vortex.Permissions;

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
            ]
        ),
        new RoleSeed(
            "economy",
            "Economy staff",
            [
                Capabilities.Economy.GrantCredits,
                Capabilities.Economy.GrantActivityPoints,
                Capabilities.Economy.GrantItem,
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
                Capabilities.Server.ConfigManage,
                Capabilities.Server.Control,
            ]
        ),
        new RoleSeed("owner", "Owner", [Capabilities.Wildcard]),
    ];
}
