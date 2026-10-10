using Vortex.Primitives.Players;
using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Combat.Zones;

namespace Vortex.Rpg.Combat.Systems;

/// <summary>The system that casts skills (spec M1b §6); the zone hands it the players' requests.
/// The last one declared answers.</summary>
public interface ICastingSystem : ICombatSystem
{
    /// <summary>A refusal code, or null when the cast is under way.</summary>
    public string? Cast(CombatZone zone, CastRequest request);

    /// <summary>The player stops his own cast.</summary>
    public void Cancel(CombatZone zone, PlayerId player);

    /// <summary>The fighter's cast is broken (an interrupt effect).</summary>
    public void Interrupt(CombatZone zone, Combatant c);

    /// <summary>A monster resets (spec M1c §7.4): its cast stops, its cooldowns are forgotten.</summary>
    public void Reset(CombatZone zone, Combatant c);
}
