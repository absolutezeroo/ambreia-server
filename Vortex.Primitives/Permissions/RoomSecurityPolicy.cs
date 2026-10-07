using Vortex.Primitives.Action;
using Vortex.Primitives.Rooms.Enums;

namespace Vortex.Primitives.Permissions;

/// <summary>
/// Pure mapping from a resolved <see cref="PermissionSet"/> (plus a subject's explicit ownership,
/// room-rights and guild standing) to an effective <see cref="RoomControllerType"/>. Staff capabilities
/// elevate the level across every room: <see cref="Capabilities.Wildcard"/> and
/// <see cref="Capabilities.Room.ModerateAny"/> reach moderator everywhere, while
/// <see cref="Capabilities.Room.BuildAny"/> reaches owner so build/pickup checks pass regardless of the
/// room owner. Guild standing layers on top of — and never displaces — the classic per-room rights
/// fallback: a rights-holder in a guild room keeps at least <see cref="RoomControllerType.Rights"/>.
/// Guilds left with the cut of 2026-10-07, so the room always passes <see cref="RoomGroupContext.None"/>.
/// </summary>
public static class RoomSecurityPolicy
{
    public static RoomControllerType ResolveControllerLevel(
        ActionOrigin origin,
        PermissionSet permissions,
        bool isExplicitOwner,
        bool hasExplicitRights,
        RoomGroupContext group = default
    )
    {
        // The default of the struct, which is nobody. Answered before System is, so that a context
        // that was never filled in gets the lowest level rather than the highest.
        if (origin == ActionOrigin.None)
        {
            return RoomControllerType.None;
        }

        if (origin == ActionOrigin.System)
        {
            return RoomControllerType.Moderator;
        }

        // Owning the room outranks holding staff powers inside it. ModerateAny is about other
        // people's rooms; checking it first made a moderator enter their own room as a moderator,
        // which is a lower standing than the owner they actually are.
        if (isExplicitOwner)
        {
            return RoomControllerType.Owner;
        }

        if (permissions.IsSuperUser || permissions.Has(Capabilities.Room.ModerateAny))
        {
            return RoomControllerType.Moderator;
        }

        if (permissions.Has(Capabilities.Room.BuildAny))
        {
            return RoomControllerType.Owner;
        }

        // Guild admins outrank plain members, who in turn outrank classic rights-holders — but only
        // while the guild actually lets members decorate. When it does not, a member falls through
        // to whatever they hold in their own right.
        if (group.IsAdmin)
        {
            return RoomControllerType.GroupAdmin;
        }

        if (group.IsMember && group.MembersCanDecorate)
        {
            return RoomControllerType.GroupRights;
        }

        if (hasExplicitRights)
        {
            return RoomControllerType.Rights;
        }

        return RoomControllerType.None;
    }

    /// <summary>
    /// "May this subject administer the room?" — rename the room, hand out rights, change its
    /// settings. Deliberately NOT the same question as
    /// <c>ResolveControllerLevel(...) &gt;= RoomControllerType.Owner</c>, which is also true for a
    /// moderator: <see cref="Capabilities.Room.ModerateAny"/> grants moderation everywhere, not the
    /// owner's settings dialog. Only explicit ownership or
    /// <see cref="Capabilities.Room.BuildAny"/> counts here.
    /// </summary>
    public static bool IsRoomOwner(PermissionSet permissions, bool isExplicitOwner)
    {
        if (isExplicitOwner)
        {
            return true;
        }

        return permissions.Has(Capabilities.Room.BuildAny);
    }
}
