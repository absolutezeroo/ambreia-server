namespace Vortex.Primitives.Permissions;

/// <summary>
/// A subject's standing in the guild that owns the room being evaluated. <see cref="None"/> is the
/// correct value both for rooms with no guild and for subjects who are not members of the owning
/// guild — in either case no group-derived controller level is granted. Guilds left with the cut of
/// 2026-10-07: the room passes <see cref="None"/> for everyone.
/// </summary>
/// <param name="IsMember">The subject holds a membership row in the owning guild.</param>
/// <param name="IsAdmin">The subject is an administrator (or owner) of the owning guild.</param>
/// <param name="MembersCanDecorate">
/// The guild lets plain members decorate its base room. Mirrors the inverse of the guild's
/// <c>admin_only_decoration</c> setting; ignored for admins, who always decorate.
/// </param>
public readonly record struct RoomGroupContext(bool IsMember, bool IsAdmin, bool MembersCanDecorate)
{
    public static readonly RoomGroupContext None = new(false, false, false);
}
