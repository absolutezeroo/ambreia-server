using System.Collections.Frozen;
using System.Collections.Generic;
using Vortex.Rpg.Content.Definitions;

namespace Vortex.Rpg.Combat.Combatants;

/// <summary>What a monster's rank does when it comes in (spec M1c §7.1): its health, damage and XP
/// multiplied, the status types it ignores.</summary>
public sealed record RankScale(
    double Health,
    double Damage,
    double Xp,
    IReadOnlySet<StatusType> Immunities
)
{
    public static readonly RankScale None = new(1, 1, 1, FrozenSet<StatusType>.Empty);
}
