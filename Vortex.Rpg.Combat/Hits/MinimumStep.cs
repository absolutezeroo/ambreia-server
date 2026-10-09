using System;

namespace Vortex.Rpg.Combat.Hits;

/// <summary>A hit that lands does at least 1.</summary>
public sealed class MinimumStep : IHitStep
{
    public int Priority => 900;

    public bool Apply(HitContext hit)
    {
        hit.Damage = Math.Max(1, Math.Round(hit.Damage, MidpointRounding.AwayFromZero));

        return true;
    }
}
