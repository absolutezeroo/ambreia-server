using System.Collections.Immutable;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms;

namespace Vortex.Primitives.Events.Combat;

/// <summary>A monster died (spec M1a §8.2): its content id and level, the zone, and every player who
/// hit it and was still there — quest "kill" objectives (M3) and loot (M2) listen to it.</summary>
public sealed record MonsterKilledEvent(
    string MonsterId,
    int Level,
    RoomId Zone,
    ImmutableArray<PlayerId> Participants
) : IEvent;
