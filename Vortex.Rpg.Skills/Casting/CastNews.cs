using System.Collections.Generic;
using Vortex.Protocol.Messages.Outgoing.Rpg.Skills;
using Vortex.Rpg.Combat.Zones;

namespace Vortex.Rpg.Skills.Casting;

/// <summary>The casts that began and ended in a zone this tick (spec M1b §10.2): one
/// <c>RpgCastStart</c> and one <c>RpgCastEnd</c> at most, a part of the zone.</summary>
public sealed class CastNews
{
    private readonly List<RpgCastStart> _starts = [];
    private readonly List<RpgCastEnd> _ends = [];

    public void Started(RpgCastStart start) => _starts.Add(start);

    public void Ended(RpgCastEnd end) => _ends.Add(end);

    public void Clear()
    {
        _starts.Clear();
        _ends.Clear();
    }

    /// <summary>Queued with the tick's messages, then forgotten.</summary>
    public void Flush(CombatZone zone)
    {
        if (_starts.Count > 0)
        {
            zone.Broadcast(new RpgCastStartMessageComposer { Casts = [.. _starts] });
        }

        if (_ends.Count > 0)
        {
            zone.Broadcast(new RpgCastEndMessageComposer { Ends = [.. _ends] });
        }

        Clear();
    }
}
