using System.Collections.Generic;
using Vortex.Primitives.Players;
using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Combat.Systems;
using Vortex.Rpg.Combat.Zones;

namespace Vortex.Rpg.Monsters.Tests.Support;

/// <summary>Stands for the skills module's casting system: records each cast asked, refuses the
/// skills <see cref="Refusals"/> names with their code, and each reset.</summary>
internal sealed class RecordingCasting : ICastingSystem
{
    public List<CastRequest> Casts { get; } = [];

    public List<int> Resets { get; } = [];

    public Dictionary<string, string> Refusals { get; } = [];

    public int Order => 200;

    public void Tick(CombatZone zone, long now) { }

    public string? Cast(CombatZone zone, CastRequest request)
    {
        Casts.Add(request);

        return Refusals.GetValueOrDefault(request.SkillId);
    }

    public void Cancel(CombatZone zone, PlayerId player) { }

    public void Interrupt(CombatZone zone, Combatant c) { }

    public void Reset(CombatZone zone, Combatant c) => Resets.Add(c.ObjectId);
}
