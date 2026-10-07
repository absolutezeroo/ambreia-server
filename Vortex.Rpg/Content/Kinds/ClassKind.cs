using System.Collections.Generic;
using System.Linq;
using Vortex.Primitives.Rpg.Content;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Content.Validation;

namespace Vortex.Rpg.Content.Kinds;

/// <summary>Classes (spec Personnage §4).</summary>
public sealed class ClassKind : ContentKind<ClassDefinition>
{
    public override string Kind => "class";

    protected override IEnumerable<ContentFinding> Check(ClassDefinition c, ContentCheck check)
    {
        if (c.Range < 1)
        {
            yield return Error(
                "BadRange",
                c.Id,
                $"Range {c.Range}: a class reaches at least 1 tile."
            );
        }

        StatBlock b = c.Base;

        if (new[] { b.Strength, b.Intelligence, b.Agility, b.Health, b.Mana }.Any(v => v <= 0))
        {
            yield return Error(
                "NonPositiveBase",
                c.Id,
                "Strength, intelligence, agility, health and mana at level 1 must all be above 0."
            );
        }

        StatBlock g = c.PerLevel;

        if (
            new[] { g.Strength, g.Intelligence, g.Agility, g.Health, g.Mana, g.Armor, g.Speed }.Any(
                v => v < 0
            )
        )
        {
            yield return Error(
                "NegativeStat",
                c.Id,
                "A class does not lose stats as it levels up."
            );
        }

        if (c.Specializations.Count == 0)
        {
            yield return Error(
                "NoSpecialization",
                c.Id,
                "A class has at least one specialization."
            );
        }

        foreach (string id in c.Specializations)
        {
            SpecializationDefinition? spec = check.Get<SpecializationDefinition>(id);

            if (spec is null)
            {
                yield return Error(
                    "BrokenReference",
                    c.Id,
                    $"Specialization '{id}' does not exist."
                );
            }
            else if (spec.Class != c.Id)
            {
                yield return Error(
                    "SpecializationOfAnotherClass",
                    c.Id,
                    $"Specialization '{id}' belongs to '{spec.Class}'."
                );
            }
        }
    }
}
