using System.Collections.Generic;
using Vortex.Protocol.Messages.Outgoing.Rpg.Skills;
using Vortex.Rpg.Combat.Zones;

namespace Vortex.Rpg.Skills.Casting;

/// <summary>The telegraphs that began in a zone this tick (spec M1c §9.2): one
/// <c>RpgTelegraphs</c> at most, a part of the zone.</summary>
public sealed class TelegraphNews
{
    private readonly List<RpgTelegraph> _started = [];

    public void Started(RpgTelegraph telegraph) => _started.Add(telegraph);

    /// <summary>A cast that ended the tick it began: its telegraph never goes out, or it would reach
    /// the client after the end that clears it.</summary>
    public void Ended(int casterObjectId) =>
        _started.RemoveAll(t => t.CasterObjectId == casterObjectId);

    public void Clear() => _started.Clear();

    /// <summary>Queued with the tick's messages, then forgotten.</summary>
    public void Flush(CombatZone zone)
    {
        if (_started.Count > 0)
        {
            zone.Broadcast(new RpgTelegraphsMessageComposer { Telegraphs = [.. _started] });
        }

        Clear();
    }
}
