using Vortex.Rpg.Combat.Combatants;

namespace Vortex.Rpg.Combat.Stats;

/// <summary>Something that changes a fighter's stats while it lasts (statuses, plan M1b), declared by
/// DI. Asked each time the zone recomputes that fighter.</summary>
public interface IStatModifierSource
{
    public void Contribute(Combatant c, StatModifiers into);
}
