using System.Collections.Generic;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Rpg.Characters;
using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Combat.Zones;

namespace Vortex.Rpg.Combat.Systems;

/// <summary>
/// A part of a zone's fights that a module brings (plan M1b, Task 4) — statuses, casts — declared by
/// DI; the zone names none. Ticked in <see cref="Order"/> before the automatic hits; told when a
/// fighter enters, dies or leaves. One instance serves every zone: its state lives in the fighters'
/// parts (<see cref="Combatant.Get{T}"/>).
/// </summary>
public interface ICombatSystem
{
    /// <summary>Lower ticks first.</summary>
    public int Order { get; }

    public void Tick(CombatZone zone, long now);

    /// <summary>A fighter joined: a player with his profile, a monster with none.</summary>
    public void Added(CombatZone zone, Combatant c, CharacterCombatProfile? profile) { }

    /// <summary>A fighter dies or leaves, still in the zone: what it carries out to its character
    /// (a player leaving), else nothing.</summary>
    public IEnumerable<CarriedStatus> Removing(CombatZone zone, Combatant c, CombatantExit exit) =>
        [];

    /// <summary>The player walked on his own.</summary>
    public void PlayerWalked(CombatZone zone, Combatant c) { }

    /// <summary>The tick's messages are about to go: queue this system's.</summary>
    public void BeforeFlush(CombatZone zone) { }

    /// <summary>For the zone's entry burst.</summary>
    public IEnumerable<IComposer> Entry(CombatZone zone) => [];
}
