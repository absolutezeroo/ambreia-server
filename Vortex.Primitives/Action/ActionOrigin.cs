namespace Vortex.Primitives.Action;

/// <summary>
/// Who is asking. Read by <see cref="Vortex.Primitives.Permissions.RoomSecurityPolicy"/>, so it is
/// the most privileged field on a struct that crosses grain boundaries.
/// </summary>
/// <remarks>
/// <see cref="None"/> holds zero deliberately. <see cref="System"/> did, and
/// <see cref="Vortex.Primitives.Action.ActionContext"/> is a <c>readonly record struct</c>: a
/// zero-initialised one -- a default parameter, an uninitialised field, a record that forgot to set
/// it -- arrived as the origin that short-circuits every permission check to moderator. Every
/// construction site was checked and none of them did that (ROOMM-ORIGIN-042); the value most
/// likely to be produced by accident is now the one that is refused everywhere.
/// </remarks>
public enum ActionOrigin
{
    /// <summary>Nobody. The default of the struct, and it may do nothing.</summary>
    None = 0,
    Player = 1,
    Plugin = 2,

    // 3 was Wired (removed with the wired system). Retired, never reused: the value crosses grains.

    Bot = 4,

    /// <summary>The server acting on its own behalf. Trusted everywhere, hence not zero.</summary>
    System = 5,
}
