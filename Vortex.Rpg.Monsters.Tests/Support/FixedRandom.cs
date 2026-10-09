using System;
using System.Collections.Generic;

namespace Vortex.Rpg.Monsters.Tests.Support;

/// <summary>A random source that answers the given values in turn (then 0.5): NextDouble returns
/// them, Next(min, max) maps them onto the range.</summary>
internal sealed class FixedRandom(params double[] values) : Random
{
    private readonly Queue<double> _values = new(values);

    public override double NextDouble() => _values.Count > 0 ? _values.Dequeue() : 0.5;

    public override int Next(int minValue, int maxValue) =>
        minValue + (int)(NextDouble() * (maxValue - minValue));
}
