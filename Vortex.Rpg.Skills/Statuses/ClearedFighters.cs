using System.Collections.Generic;

namespace Vortex.Rpg.Skills.Statuses;

/// <summary>Monsters that died this tick carrying statuses, a part of the zone: no longer fighters,
/// their bodies are still shown, and shown bare (spec M1b §7.4).</summary>
public sealed class ClearedFighters
{
    public List<int> ObjectIds { get; } = [];
}
