using Vortex.Primitives.Players;

namespace Vortex.Primitives.Events.Rpg;

/// <summary>A character went up one level; a grant over several levels publishes one per level.</summary>
public sealed record LevelUpEvent(PlayerId PlayerId, string ClassId, int OldLevel, int NewLevel)
    : IEvent;
