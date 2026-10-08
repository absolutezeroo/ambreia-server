using Vortex.Primitives.Players;

namespace Vortex.Primitives.Events.Rpg;

/// <summary>XP was granted: the amount given and where the character now stands.</summary>
public sealed record XpGainedEvent(PlayerId PlayerId, int Amount, string Source, int Level, int Xp)
    : IEvent;
