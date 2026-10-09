using System;
using System.Collections.Generic;
using System.Linq;
using Vortex.Rpg.Content;
using Vortex.Rpg.Content.Definitions;

namespace Vortex.Rpg.Skills;

/// <summary>
/// The skills a character knows (spec M1b §9): those of its class — and of its specialization when a
/// skill names some — at or below its level. Worked out from the content each time, never stored: a
/// level gained or a class chosen changes them at once.
/// </summary>
public static class KnownSkills
{
    public static IReadOnlyList<SkillDefinition> For(
        IRpgContent content,
        string classId,
        string specializationId,
        int level
    ) =>
        [
            .. content
                .All<SkillDefinition>()
                .Where(s =>
                    !s.Retired
                    && s.Level <= level
                    && s.Classes.Contains(classId)
                    && (
                        s.Specializations.Count == 0 || s.Specializations.Contains(specializationId)
                    )
                )
                .OrderBy(s => s.Level)
                .ThenBy(s => s.Id, StringComparer.Ordinal),
        ];
}
