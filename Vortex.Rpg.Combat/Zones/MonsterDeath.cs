using System.Collections.Generic;
using Vortex.Primitives.Players;

namespace Vortex.Rpg.Combat.Zones;

/// <summary>A monster that died this tick: which one, and the players present who fought it.</summary>
public sealed record MonsterDeath(
    int ObjectId,
    string MonsterId,
    IReadOnlyList<PlayerId> Participants
);
