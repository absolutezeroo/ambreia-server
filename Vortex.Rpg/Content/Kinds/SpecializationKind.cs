using System.Collections.Generic;
using Vortex.Primitives.Rpg.Content;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Content.Validation;

namespace Vortex.Rpg.Content.Kinds;

/// <summary>Specializations (spec Personnage §4): for now, only the class they belong to.</summary>
public sealed class SpecializationKind : ContentKind<SpecializationDefinition>
{
    public override string Kind => "specialization";

    protected override IEnumerable<ContentFinding> Check(
        SpecializationDefinition s,
        ContentCheck check
    )
    {
        if (!check.Is<ClassDefinition>(s.Class))
        {
            yield return Error("BrokenReference", s.Id, $"Class '{s.Class}' does not exist.");
        }
    }
}
