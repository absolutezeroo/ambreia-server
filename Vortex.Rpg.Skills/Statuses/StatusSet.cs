using System;
using System.Collections.Generic;
using System.Linq;
using Vortex.Rpg.Content.Definitions;

namespace Vortex.Rpg.Skills.Statuses;

/// <summary>The statuses on one fighter, kept as its part (spec M1b §7); <see cref="Changed"/> until
/// the zone has been told.</summary>
public sealed class StatusSet
{
    private readonly List<ActiveStatus> _all = [];
    private long _sequence;

    public IReadOnlyList<ActiveStatus> All => _all;

    public bool Changed { get; set; }

    public ActiveStatus? Find(string statusId) => _all.Find(s => s.Definition.Id == statusId);

    public long NextSequence() => ++_sequence;

    public void Add(ActiveStatus status)
    {
        _all.Add(status);
        Changed = true;
    }

    public void Remove(ActiveStatus status)
    {
        if (_all.Remove(status))
        {
            Changed = true;
        }
    }

    public void Clear()
    {
        if (_all.Count > 0)
        {
            _all.Clear();
            Changed = true;
        }
    }

    /// <summary>Shields take <paramref name="damage"/>, the oldest first; an exhausted one goes.
    /// What they took.</summary>
    public double Absorb(double damage)
    {
        double taken = 0;

        foreach (
            ActiveStatus shield in _all.Where(s => s.Definition.Type == StatusType.Shield)
                .OrderBy(s => s.Sequence)
                .ToArray()
        )
        {
            double share = Math.Min(shield.Amount, damage - taken);
            shield.Amount -= share;
            taken += share;

            if (shield.Amount <= 0)
            {
                Remove(shield);
            }

            if (taken >= damage)
            {
                break;
            }
        }

        return taken;
    }
}
