using System.Collections.Generic;
using System.Linq;
using Vortex.Primitives.Rpg.Content;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Content.Rules;
using Vortex.Rpg.Content.Validation;

namespace Vortex.Rpg.Content.Kinds;

/// <summary>
/// Skills (spec M1b §5.1, §5.4). Each effect is checked by the rule of the module that runs it: an
/// effect no module declares refuses the pack, so the zone never meets a skill it cannot play.
/// </summary>
public sealed class SkillKind(IEnumerable<ISkillEffectRule> rules) : ContentKind<SkillDefinition>
{
    private readonly Dictionary<string, ISkillEffectRule> _rules = rules
        .GroupBy(r => r.Kind)
        .ToDictionary(g => g.Key, g => g.Last());

    public override string Kind => "skill";

    public override bool Public => true;

    protected override IEnumerable<ContentFinding> Check(SkillDefinition s, ContentCheck check)
    {
        if (s.Classes.Count == 0)
        {
            yield return Error("NoClass", s.Id, "A skill belongs to at least one class.");
        }

        foreach (string cls in s.Classes.Where(c => !check.Is<ClassDefinition>(c)))
        {
            yield return Error("BrokenReference", s.Id, $"Class '{cls}' does not exist.");
        }

        // A typo here would make the skill silently unknowable.
        foreach (
            string spec in s.Specializations.Where(x => !check.Is<SpecializationDefinition>(x))
        )
        {
            yield return Error("BrokenReference", s.Id, $"Specialization '{spec}' does not exist.");
        }

        if (s.Level < 1)
        {
            yield return Error(
                "BadLevel",
                s.Id,
                $"Level {s.Level}: a skill is learned at 1 or above."
            );
        }

        int[] values = [s.Cost, s.CooldownMs, s.CastMs, s.ChannelMs, s.PulseMs, s.Range, s.Radius];

        if (values.Any(v => v < 0) || s.MaxTargets < 1)
        {
            yield return Error(
                "NegativeValue",
                s.Id,
                "Cost, times, range and radius are never negative; an area reaches at least 1 target."
            );
        }

        if (s.Type == SkillType.Cast && s.CastMs <= 0)
        {
            yield return Error("CastWithoutTime", s.Id, "A cast skill needs castMs above 0.");
        }

        if (
            s.Type == SkillType.Channel
            && (s.ChannelMs <= 0 || s.PulseMs <= 0 || s.PulseMs > s.ChannelMs)
        )
        {
            yield return Error(
                "ChannelWithoutPulse",
                s.Id,
                "A channel needs channelMs above 0 and a pulseMs between 1 and channelMs."
            );
        }

        bool area =
            s.Target is SkillTarget.Ground or SkillTarget.AroundCaster or SkillTarget.AroundTarget;

        if (area && s.Radius <= 0)
        {
            yield return Error(
                "AreaWithoutRadius",
                s.Id,
                $"Target '{s.Target}' needs a radius above 0."
            );
        }

        // Cast where one stands: a range would start an approach walk for nothing.
        if (s.Target is SkillTarget.Self or SkillTarget.AroundCaster && s.Range != 0)
        {
            yield return Error(
                "RangeOnSelf",
                s.Id,
                $"Target '{s.Target}' is cast where the caster stands: range is 0."
            );
        }

        if (s.Effects.Count == 0)
        {
            yield return Error("NoEffect", s.Id, "A skill applies at least one effect.");
        }

        // "effects":[null] parses: the JSON options do not look inside a list.
        if (s.Effects.Any(e => e is null))
        {
            yield return Error("NullEntry", s.Id, "An effect is null.");

            yield break;
        }

        foreach (ContentFinding finding in s.Effects.SelectMany(e => CheckEffect(s, e, check)))
        {
            yield return finding;
        }
    }

    private IEnumerable<ContentFinding> CheckEffect(
        SkillDefinition s,
        SkillEffectDefinition e,
        ContentCheck check
    )
    {
        if (!_rules.TryGetValue(e.Kind, out ISkillEffectRule? rule))
        {
            yield return Error(
                "UnknownEffect",
                s.Id,
                $"Effect '{e.Kind}' is not declared by any module."
            );

            yield break;
        }

        foreach (string problem in rule.Problems(e))
        {
            yield return Error("BadEffect", s.Id, $"Effect '{e.Kind}': {problem}");
        }

        if (e.Status is { } status && !check.Is<StatusDefinition>(status))
        {
            yield return Error("BrokenReference", s.Id, $"Status '{status}' does not exist.");
        }
    }
}
