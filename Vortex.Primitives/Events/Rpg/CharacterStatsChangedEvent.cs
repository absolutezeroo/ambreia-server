using Vortex.Primitives.Players;

namespace Vortex.Primitives.Events.Rpg;

/// <summary>A character's stats changed (a level, equipment, the content): ask the grain again.</summary>
public sealed record CharacterStatsChangedEvent(PlayerId PlayerId) : IEvent;
