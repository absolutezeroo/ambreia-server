using Vortex.Primitives.Players.Enums;

namespace Vortex.Primitives.Permissions;

/// <summary>
/// Pure mapping from a resolved <see cref="PermissionSet"/> to the client-facing
/// <see cref="SecurityLevelType"/> sent at login. The security level only drives which moderation UI the
/// client exposes; the authoritative gate for every action stays the capability check on the server.
/// A subject with no roles resolves to <see cref="SecurityLevelType.None"/> (a normal player).
/// </summary>
public static class SecurityLevelPolicy
{
    public static SecurityLevelType Resolve(PermissionSet permissions)
    {
        if (permissions.IsSuperUser)
        {
            return SecurityLevelType.Administrator;
        }

        if (
            permissions.HasAny(
                Capabilities.Room.ModerateAny,
                Capabilities.Moderation.Kick,
                Capabilities.Moderation.Mute,
                Capabilities.Moderation.Alert,
                Capabilities.Moderation.Ban
            )
        )
        {
            return SecurityLevelType.Moderator;
        }

        return SecurityLevelType.None;
    }
}
