using Vortex.Primitives.Players;

namespace Vortex.Primitives.Events.Rpg;

/// <summary>A player's quest log changed (spec M2a §3.2): the wire and the markers follow it.</summary>
public sealed record QuestLogChangedEvent(PlayerId PlayerId) : IEvent;
