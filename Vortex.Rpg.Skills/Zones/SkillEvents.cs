using System.Collections.Generic;
using Vortex.Protocol.Messages.Outgoing.Rpg.Skills;
using Vortex.Rpg.Combat.Zones;

namespace Vortex.Rpg.Skills.Zones;

/// <summary>What skills and statuses did in a zone this tick (spec M1b §10.2): one
/// <c>RpgSkillEvents</c> when something did.</summary>
public sealed class SkillEvents
{
    private readonly List<RpgSkillImpact> _impacts = [];
    private readonly List<RpgSkillOutcome> _outcomes = [];

    public void AddImpact(RpgSkillImpact impact) => _impacts.Add(impact);

    public void AddOutcome(RpgSkillOutcome outcome) => _outcomes.Add(outcome);

    /// <summary>Queued with the tick's messages, then forgotten.</summary>
    public void Flush(CombatZone zone)
    {
        if (_impacts.Count == 0 && _outcomes.Count == 0)
        {
            return;
        }

        zone.Broadcast(
            new RpgSkillEventsMessageComposer { Impacts = [.. _impacts], Outcomes = [.. _outcomes] }
        );
        _impacts.Clear();
        _outcomes.Clear();
    }
}
