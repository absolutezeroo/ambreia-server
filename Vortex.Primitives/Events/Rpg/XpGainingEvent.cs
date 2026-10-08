using Vortex.Primitives.Players;

namespace Vortex.Primitives.Events.Rpg;

/// <summary>
/// XP is about to be granted (spec Personnage §5): cancellable, and its <see cref="Amount"/> may be
/// changed — by an event behaviour, which runs in a chain; handlers run in parallel and must not
/// write it.
/// </summary>
public sealed record XpGainingEvent(PlayerId PlayerId, string Source) : IEvent
{
    public required int Amount { get; set; }
}
