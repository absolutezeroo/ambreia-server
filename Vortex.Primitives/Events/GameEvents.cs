namespace Vortex.Primitives.Events;

/// <summary>
/// A player earned points in a room game.
/// </summary>
/// <remarks>
/// Raised from the one place every game point already passes through, the room's scoring funnel,
/// rather than from each game: a score there already carries who caused it, how much, and which
/// scoring act it was, and <c>GameScore</c>'s own documentation says that is what the achievement
/// and quest work is meant to subscribe to instead of a diff between two integers.
/// <para>
/// Only positive scores raise this. A negative one — Freeze's friendly fire — moves a team's tally
/// and nothing else: progression here never runs backwards.
/// </para>
/// </remarks>
/// <param name="Game">The game's own id, <c>freeze</c> or <c>banzai</c>.</param>
/// <param name="Reason">
/// The scoring act, in the game's vocabulary: <c>freeze.player_frozen</c>,
/// <c>banzai.tile_locked</c>. Short, lowercase and stable, because things key on it.
/// </param>
public sealed record GamePointsScoredEvent(
    int PlayerId,
    int RoomId,
    string Game,
    string Reason,
    int Amount
) : IEvent;
