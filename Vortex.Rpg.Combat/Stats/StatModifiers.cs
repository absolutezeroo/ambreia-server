using System;
using System.Collections.Generic;

namespace Vortex.Rpg.Combat.Stats;

/// <summary>
/// What every source changes on one fighter (spec M1b §7.3): flats and percents per stat, summed;
/// the strongest slow; a stun from any source.
/// </summary>
public sealed class StatModifiers
{
    private readonly Dictionary<CombatStat, double> _flat = [];
    private readonly Dictionary<CombatStat, double> _percent = [];

    public int SlowPercent { get; private set; }

    public bool Stunned { get; set; }

    public void AddFlat(CombatStat stat, double value) =>
        _flat[stat] = _flat.GetValueOrDefault(stat) + value;

    public void AddPercent(CombatStat stat, double value) =>
        _percent[stat] = _percent.GetValueOrDefault(stat) + value;

    /// <summary>Several slows: the strongest applies (spec M1b §7.2).</summary>
    public void Slow(int percent) => SlowPercent = Math.Max(SlowPercent, percent);

    /// <summary><c>(base + Σ flats) × (1 + Σ percents / 100)</c>.</summary>
    public double Apply(CombatStat stat, double baseValue) =>
        (baseValue + _flat.GetValueOrDefault(stat)) * (1 + _percent.GetValueOrDefault(stat) / 100);

    public void Merge(StatModifiers other)
    {
        foreach ((CombatStat stat, double value) in other._flat)
        {
            AddFlat(stat, value);
        }

        foreach ((CombatStat stat, double value) in other._percent)
        {
            AddPercent(stat, value);
        }

        Slow(other.SlowPercent);
        Stunned |= other.Stunned;
    }

    public void Clear()
    {
        _flat.Clear();
        _percent.Clear();
        SlowPercent = 0;
        Stunned = false;
    }
}
