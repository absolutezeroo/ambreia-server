using System.Collections.Generic;
using System.Text.RegularExpressions;
using Vortex.Rpg.Content.Definitions;

namespace Vortex.Rpg.Content.Validation;

/// <summary>The shape of a visual's phases: a sheet named <c>&lt;folder&gt;/&lt;name&gt;</c>, delays and speeds that make sense.</summary>
public static partial class VfxCheck
{
    [GeneratedRegex("^[a-z0-9_-]+/[a-z0-9_-]+$")]
    private static partial Regex SheetName();

    public static IEnumerable<string> Problems(SkillVfx? vfx)
    {
        if (vfx is null)
        {
            yield break;
        }

        foreach (
            (string phase, VfxPhase? p) in new[]
            {
                ("cast", vfx.Cast),
                ("release", vfx.Release),
                ("area", vfx.Area),
                ("hit", vfx.Hit),
            }
        )
        {
            foreach (string problem in Problems(phase, p))
            {
                yield return problem;
            }
        }

        if (vfx.Projectile is { } m)
        {
            if (!SheetName().IsMatch(m.Sheet))
            {
                yield return $"projectile: sheet '{m.Sheet}' is not <folder>/<name>.";
            }

            if (m.Speed <= 0)
            {
                yield return "projectile: speed is above 0.";
            }
        }
    }

    public static IEnumerable<string> Problems(StatusVfx? vfx) =>
        vfx is null ? [] : Problems("aura", vfx.Aura);

    private static IEnumerable<string> Problems(string phase, VfxPhase? p)
    {
        if (p is null)
        {
            yield break;
        }

        if (!SheetName().IsMatch(p.Sheet))
        {
            yield return $"{phase}: sheet '{p.Sheet}' is not <folder>/<name>.";
        }

        if (p.DelayMs < 0)
        {
            yield return $"{phase}: delayMs is never negative.";
        }
    }
}
