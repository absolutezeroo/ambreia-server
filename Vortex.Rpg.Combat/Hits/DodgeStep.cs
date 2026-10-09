namespace Vortex.Rpg.Combat.Hits;

/// <summary>The target dodges by its dodge chance: a miss, no damage, the chain stops.</summary>
public sealed class DodgeStep : IHitStep
{
    public int Priority => 100;

    public bool Apply(HitContext hit)
    {
        if (hit.Options.Unavoidable || hit.Random.NextDouble() * 100 >= hit.Target.DodgePercent)
        {
            return true;
        }

        hit.Outcome = HitContext.Miss;
        hit.Damage = 0;

        return false;
    }
}
