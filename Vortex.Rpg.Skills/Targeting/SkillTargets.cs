using System.Collections.Generic;
using System.Linq;
using Vortex.Rpg.Combat;
using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Combat.Zones;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Skills.Effects;
using Vortex.Rpg.Zones;

namespace Vortex.Rpg.Skills.Targeting;

/// <summary>
/// Whom a skill touches when it lands (spec M1b §6.3, §6.6): oneself, the ally or the enemy aimed
/// at, or an area's fighters of the skill's camp — alive, within its radius (Chebyshev), in sight of
/// its centre — nearest first, at most its <c>maxTargets</c>.
/// </summary>
public static class SkillTargets
{
    public static IReadOnlyList<Combatant> Resolve(
        SkillContext ctx,
        int targetObjectId,
        (int X, int Y)? cell
    )
    {
        CombatZone zone = ctx.Zone;
        SkillDefinition skill = ctx.Skill;

        switch (skill.Target)
        {
            case SkillTarget.Self:
                return [ctx.Caster];
            case SkillTarget.Ally:
                Combatant? ally = targetObjectId == 0 ? ctx.Caster : Find(zone, targetObjectId);

                return ally is not null && IsAlly(ctx.Caster, ally) ? [ally] : [];
            case SkillTarget.Enemy:
                return Find(zone, targetObjectId) is { } enemy && IsEnemy(ctx.Caster, enemy)
                    ? [enemy]
                    : [];
            case SkillTarget.Ground:
                return cell is { } at ? Area(ctx, at) : [];
            case SkillTarget.AroundCaster:
                return zone.Runtime.PositionOf(ctx.Caster.ObjectId) is { } me ? Area(ctx, me) : [];
            case SkillTarget.AroundTarget:
                return
                    Find(zone, targetObjectId) is { } centre
                    && IsEnemy(ctx.Caster, centre)
                    && zone.Runtime.PositionOf(centre.ObjectId) is { } there
                    ? Area(ctx, there)
                    : [];
            default:
                return [];
        }
    }

    /// <summary>The caster's side, standing (spec M1c §4.2): no PvP, players help players and monsters
    /// monsters; a monster walking home takes nothing.</summary>
    public static bool IsAlly(Combatant caster, Combatant c) =>
        c.IsPlayer == caster.IsPlayer && Standing(c);

    /// <summary>The other side, standing: a player alive and not a ghost, a monster alive and not
    /// walking home.</summary>
    public static bool IsEnemy(Combatant caster, Combatant c) =>
        c.IsPlayer != caster.IsPlayer && Standing(c);

    private static bool Standing(Combatant c) => !c.IsDead && !c.IsGhost && !c.Invulnerable;

    /// <summary>A telegraph landing (spec M1c §8.2): the fighters of the skill's camp standing on its
    /// cells — never on a safe cell for a harmful one — nearest the origin first, at most its
    /// <c>maxTargets</c>.</summary>
    public static IReadOnlyList<Combatant> InCells(
        SkillContext ctx,
        IReadOnlyList<(int X, int Y)> cells,
        (int X, int Y) origin
    )
    {
        bool offensive = ctx.Effects.IsOffensive(ctx.Skill, ctx.Content);
        IZoneRuntime runtime = ctx.Zone.Runtime;
        HashSet<(int X, int Y)> marked = [.. cells];

        return
        [
            .. ctx
                .Zone.Combatants.Values.Where(c =>
                    offensive ? IsEnemy(ctx.Caster, c) : IsAlly(ctx.Caster, c)
                )
                .Select(c => (Fighter: c, At: runtime.PositionOf(c.ObjectId)))
                .Where(x =>
                    x.At is { } at
                    && marked.Contains(at)
                    && !(offensive && ctx.Zone.IsSafeCell(at.X, at.Y))
                )
                .OrderBy(x =>
                    (x.At!.Value.X - origin.X) * (x.At.Value.X - origin.X)
                    + (x.At.Value.Y - origin.Y) * (x.At.Value.Y - origin.Y)
                )
                .ThenBy(x => x.Fighter.ObjectId)
                .Take(ctx.Skill.MaxTargets)
                .Select(x => x.Fighter),
        ];
    }

    private static Combatant? Find(CombatZone zone, int objectId) =>
        zone.Combatants.TryGetValue(objectId, out Combatant? c) ? c : null;

    private static List<Combatant> Area(SkillContext ctx, (int X, int Y) centre)
    {
        bool offensive = ctx.Effects.IsOffensive(ctx.Skill, ctx.Content);
        IZoneRuntime runtime = ctx.Zone.Runtime;

        return
        [
            .. ctx
                .Zone.Combatants.Values.Where(c =>
                    offensive ? IsEnemy(ctx.Caster, c) : IsAlly(ctx.Caster, c)
                )
                .Select(c => (Fighter: c, At: runtime.PositionOf(c.ObjectId)))
                .Where(x =>
                    x.At is { } at
                    && CombatMath.Chebyshev(centre.X, centre.Y, at.X, at.Y) <= ctx.Skill.Radius
                    && runtime.HasLineOfSight(centre.X, centre.Y, at.X, at.Y)
                    // No combat on a safe cell (spec M1a §5.3): a monster's area spares it.
                    && !(offensive && ctx.Zone.IsSafeCell(at.X, at.Y))
                )
                .OrderBy(x => CombatMath.Chebyshev(centre.X, centre.Y, x.At!.Value.X, x.At.Value.Y))
                .ThenBy(x => x.Fighter.ObjectId)
                .Take(ctx.Skill.MaxTargets)
                .Select(x => x.Fighter),
        ];
    }
}
