using Vortex.Protocol.Messages.Outgoing.Rpg.Skills;
using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Combat.Zones;
using Vortex.Rpg.Content.Definitions;

namespace Vortex.Rpg.Skills.Targeting;

/// <summary>
/// The target and safe-cell checks of a cast (spec M1b §6.1, steps 3 and 4): the target fits the
/// mode; an offensive skill neither leaves nor reaches a safe cell. Range and sight are the cast's
/// business: out of them, the caster walks.
/// </summary>
public static class TargetCheck
{
    /// <summary>A refusal code, or null.</summary>
    public static string? Validate(
        CombatZone zone,
        SkillDefinition skill,
        bool offensive,
        Combatant caster,
        int targetObjectId,
        (int X, int Y)? cell
    )
    {
        Combatant? target = zone.Combatants.TryGetValue(targetObjectId, out Combatant? c)
            ? c
            : null;

        bool valid = skill.Target switch
        {
            SkillTarget.Self or SkillTarget.AroundCaster => true,
            SkillTarget.Ally => targetObjectId == 0
                || target is not null && SkillTargets.IsAlly(caster, target),
            SkillTarget.Enemy or SkillTarget.AroundTarget => target is not null
                && SkillTargets.IsEnemy(caster, target),
            SkillTarget.Ground => cell is { } at && zone.Runtime.IsWalkable(at.X, at.Y),
            _ => false,
        };

        if (!valid)
        {
            return RpgSkillResultMessageComposer.InvalidTarget;
        }

        if (!offensive)
        {
            return null;
        }

        bool safe =
            Safe(zone, caster.ObjectId)
            || target is not null
                && skill.Target is SkillTarget.Enemy or SkillTarget.AroundTarget
                && Safe(zone, target.ObjectId)
            || skill.Target == SkillTarget.Ground
                && cell is { } ground
                && zone.IsSafeCell(ground.X, ground.Y);

        return safe ? RpgSkillResultMessageComposer.SafeZone : null;
    }

    private static bool Safe(CombatZone zone, int objectId) =>
        zone.Runtime.PositionOf(objectId) is { } at && zone.IsSafeCell(at.X, at.Y);
}
