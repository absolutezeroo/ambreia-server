using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms;

namespace Vortex.Primitives.Events.Combat;

/// <summary>A player died in a zone (spec M1a §8.1); the killer is a monster's content id, or null.</summary>
public sealed record PlayerDiedEvent(PlayerId Player, RoomId Zone, string? KillerMonsterId)
    : IEvent;
