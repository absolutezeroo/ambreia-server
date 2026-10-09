using System.Collections.Generic;

namespace Vortex.Rpg.Combat.Combatants;

/// <summary>A monster's threat per player (spec M1a §6.3): it aims at the top; on a tie, at whoever
/// came first. Bounded by the players in the zone.</summary>
public sealed class ThreatTable
{
    private readonly List<(int ObjectId, long Threat)> _entries = [];

    public bool IsEmpty => _entries.Count == 0;

    public int? Top
    {
        get
        {
            int? top = null;
            long best = long.MinValue;

            foreach ((int id, long threat) in _entries)
            {
                if (threat > best)
                {
                    (top, best) = (id, threat);
                }
            }

            return top;
        }
    }

    public bool Contains(int objectId) => _entries.FindIndex(e => e.ObjectId == objectId) >= 0;

    public void Add(int objectId, long amount)
    {
        int at = _entries.FindIndex(e => e.ObjectId == objectId);

        if (at < 0)
        {
            _entries.Add((objectId, amount));
        }
        else
        {
            _entries[at] = (objectId, _entries[at].Threat + amount);
        }
    }

    public void Remove(int objectId) => _entries.RemoveAll(e => e.ObjectId == objectId);

    public void Clear() => _entries.Clear();
}
