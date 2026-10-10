using System.Collections.Generic;
using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Combat.Systems;
using Vortex.Rpg.Combat.Zones;

namespace Vortex.Rpg.Monsters.Tests.Support;

/// <summary>Stands for the skills module's status system: records "apply status.x 12 permanent" and
/// "remove status.x 12".</summary>
internal sealed class RecordingStatuses : IStatusSystem
{
    public List<string> Log { get; } = [];

    public int Order => 100;

    public void Tick(CombatZone zone, long now) { }

    public void Apply(
        CombatZone zone,
        Combatant source,
        Combatant target,
        string statusId,
        bool permanent
    ) => Log.Add($"apply {statusId} {target.ObjectId}{(permanent ? " permanent" : "")}");

    public void Remove(CombatZone zone, Combatant target, string statusId) =>
        Log.Add($"remove {statusId} {target.ObjectId}");
}
