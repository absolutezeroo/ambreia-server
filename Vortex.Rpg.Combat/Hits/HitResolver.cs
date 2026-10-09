using System;
using System.Collections.Generic;
using System.Linq;

namespace Vortex.Rpg.Combat.Hits;

/// <summary>Runs a hit through the declared steps, by priority then full type name (spec M1a §6.2).</summary>
public sealed class HitResolver
{
    private readonly IHitStep[] _steps;

    public HitResolver(IEnumerable<IHitStep> steps)
    {
        _steps =
        [
            .. steps
                .OrderBy(s => s.Priority)
                .ThenBy(s => s.GetType().FullName, StringComparer.Ordinal),
        ];
    }

    /// <summary>The steps in the order they run, for the start-up log.</summary>
    public IReadOnlyList<IHitStep> Steps => _steps;

    public (int Amount, string Outcome) Resolve(
        ICombatantStats attacker,
        ICombatantStats target,
        Random random
    )
    {
        HitContext hit = new()
        {
            Attacker = attacker,
            Target = target,
            Random = random,
        };

        foreach (IHitStep step in _steps)
        {
            if (!step.Apply(hit))
            {
                break;
            }
        }

        return (
            Math.Max(0, (int)Math.Round(hit.Damage, MidpointRounding.AwayFromZero)),
            hit.Outcome
        );
    }
}
