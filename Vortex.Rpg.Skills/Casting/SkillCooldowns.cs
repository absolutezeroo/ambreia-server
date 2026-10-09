using System;
using System.Collections.Generic;

namespace Vortex.Rpg.Skills.Casting;

/// <summary>A fighter's global cooldown and each skill's own (spec M1b §6.5), a part of the fighter:
/// they stay with the zone.</summary>
public sealed class SkillCooldowns
{
    public long GlobalUntilMs { get; set; }

    public Dictionary<string, long> Until { get; } = new(StringComparer.Ordinal);
}
