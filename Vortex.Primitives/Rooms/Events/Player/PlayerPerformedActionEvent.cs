namespace Vortex.Primitives.Rooms.Events.Player;

/// <summary>
/// Raised when a player deliberately performs one of the actions the client's
/// user-action catalogue names — an expression, a sign, a dance.
/// </summary>
/// <remarks>
/// The codes are the client's, not ours: 0 wave, 1 blow, 2 laugh, 3 respect, 5 sleep, 10 sign,
/// 11 dance. They are what the wired box stores when the room owner picks an action in the form, so
/// translating at the edge — once, where the action happens — keeps every reader on the same
/// vocabulary.
/// <para>
/// Postures (6 sit, 7 stand, 8 lay) are absent on purpose: nobody performs them, they fall out of
/// walking onto a chair or a bed, and the seam for those is <c>RoomAvatar.Sit</c>/<c>Lay</c>, which
/// has no room event bus to publish on. The wired *condition* answers them already, so a stack that
/// needs "is sitting" can ask rather than wait.
/// </para>
/// </remarks>
public sealed record PlayerPerformedActionEvent : PlayerEvent
{
    public required int ActionCode { get; init; }

    /// <summary>
    /// The sign or dance the player picked, for the two actions that carry one. <c>-1</c> for every
    /// other action, and for a box that does not narrow to a particular one.
    /// </summary>
    public int Extra { get; init; } = -1;
}
