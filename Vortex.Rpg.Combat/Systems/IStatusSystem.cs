using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Combat.Zones;

namespace Vortex.Rpg.Combat.Systems;

/// <summary>The system that keeps statuses (spec M1b §7), as another module asks it through the zone
/// (a boss's phase, spec M1c §7.2). The last one declared answers.</summary>
public interface IStatusSystem : ICombatSystem
{
    /// <summary>Puts a status of the content on the target; <paramref name="permanent"/>: until taken
    /// off. An unknown id puts nothing.</summary>
    public void Apply(
        CombatZone zone,
        Combatant source,
        Combatant target,
        string statusId,
        bool permanent
    );

    public void Remove(CombatZone zone, Combatant target, string statusId);
}
