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

    /// <summary>An automatic swing: the damage the target takes, after any shield.</summary>
    public (int Amount, string Outcome) Resolve(
        ICombatantStats attacker,
        ICombatantStats target,
        Random random
    )
    {
        (int amount, _, string outcome) = Resolve(attacker, target, random, HitOptions.None);

        return (amount, outcome);
    }

    /// <summary>A hit shaped by <paramref name="options"/> (a skill, a status): the damage the
    /// target takes and what a shield took instead.</summary>
    public (int Amount, int Absorbed, string Outcome) Resolve(
        ICombatantStats attacker,
        ICombatantStats target,
        Random random,
        HitOptions options
    )
    {
        HitContext hit = new()
        {
            Attacker = attacker,
            Target = target,
            Random = random,
            Options = options,
        };

        foreach (IHitStep step in _steps)
        {
            if (!step.Apply(hit))
            {
                break;
            }
        }

        return (Round(hit.Damage), Round(hit.Absorbed), hit.Outcome);
    }

    private static int Round(double value) =>
        Math.Max(0, (int)Math.Round(value, MidpointRounding.AwayFromZero));
}
