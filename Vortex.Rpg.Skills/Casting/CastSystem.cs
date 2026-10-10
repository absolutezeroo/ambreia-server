using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rpg.Characters;
using Vortex.Protocol.Messages.Outgoing.Rpg.Skills;
using Vortex.Rpg.Combat;
using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Combat.Hits;
using Vortex.Rpg.Combat.Systems;
using Vortex.Rpg.Combat.Zones;
using Vortex.Rpg.Content;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Learning;
using Vortex.Rpg.Skills.Configuration;
using Vortex.Rpg.Skills.Effects;
using Vortex.Rpg.Skills.Statuses;
using Vortex.Rpg.Skills.Targeting;
using Vortex.Rpg.Zones;

namespace Vortex.Rpg.Skills.Casting;

/// <summary>
/// Players' casts (spec M1b §6): the checks in their order, the approach, an instant landing at the
/// next tick, a cast at its end, a channel by pulses, and everything that stops one. The state lives
/// in the casters' parts; one instance serves every zone.
/// </summary>
public sealed class CastSystem(
    SkillEffects effects,
    HitResolver hits,
    StatusApplier statuses,
    IOptions<SkillsConfig> config,
    IRpgContent content,
    IVortexMetrics metrics
) : ICastingSystem
{
    private const string Resolved = "resolved";

    private readonly SkillsConfig _config = config.Value;

    public int Order => 200;

    public string? Cast(CombatZone zone, CastRequest request)
    {
        string? refusal = Accept(zone, request);
        Combatant? caster = zone.Combatants.TryGetValue(request.CasterObjectId, out Combatant? c)
            ? c
            : null;

        // A monster tries its skills every tick: its refusals would drown the metric (plan M1c
        // decision 2). A client string never becomes a label: an id nobody declared is "unknown".
        if (refusal is not null && caster is { IsPlayer: true })
        {
            string label = content.Get<SkillDefinition>(request.SkillId) is null
                ? "unknown"
                : request.SkillId;
            metrics.SkillCast(label, refusal);
        }

        if (caster is not null)
        {
            Steer(caster);
        }

        return refusal;
    }

    public void Reset(CombatZone zone, Combatant c)
    {
        End(zone, c, RpgCastEnd.Cancelled);
        c.Set<PendingCast>(null);
        c.Set<SkillCooldowns>(null);
        Steer(c);
    }

    public void Cancel(CombatZone zone, PlayerId player)
    {
        if (zone.PlayerCombatant(player) is { } c)
        {
            End(zone, c, RpgCastEnd.Cancelled);
            DropApproach(c);
        }
    }

    public void Interrupt(CombatZone zone, Combatant c) => End(zone, c, RpgCastEnd.Interrupted);

    public void PlayerWalked(CombatZone zone, Combatant c)
    {
        if (c.Get<CastState>() is { Skill.MoveAllowed: false })
        {
            End(zone, c, RpgCastEnd.Cancelled);
        }

        DropApproach(c);
    }

    public void Added(CombatZone zone, Combatant c, CharacterCombatProfile? profile)
    {
        // The first one in hears nothing from the empty zone's past.
        if (c.IsPlayer && zone.Players.Count() == 1)
        {
            zone.GetOrAdd<CastNews>().Clear();
        }
    }

    public IEnumerable<CarriedStatus> Removing(CombatZone zone, Combatant c, CombatantExit exit)
    {
        End(zone, c, RpgCastEnd.Cancelled);
        c.Set<PendingCast>(null);
        Steer(c);

        return [];
    }

    public void BeforeFlush(CombatZone zone) => zone.GetOrAdd<CastNews>().Flush(zone);

    public void Tick(CombatZone zone, long now)
    {
        foreach (Combatant c in zone.Combatants.Values.ToArray())
        {
            if (c.Get<CastState>() is { } state)
            {
                TickCast(zone, c, state, now);
            }
            else if (c.Get<PendingCast>() is { } pending)
            {
                TickPending(zone, c, pending, now);
            }

            Steer(c);
        }
    }

    /// <summary>Steps 1–5 of spec §6.1, then the approach or the start.</summary>
    private string? Accept(CombatZone zone, CastRequest request)
    {
        if (
            !zone.Combatants.TryGetValue(request.CasterObjectId, out Combatant? caster)
            || caster.IsDead
            || caster.IsGhost
        )
        {
            return RpgSkillResultMessageComposer.Dead;
        }

        if (caster.Stunned)
        {
            return RpgSkillResultMessageComposer.Stunned;
        }

        if (caster.Get<CastState>() is not null)
        {
            return RpgSkillResultMessageComposer.Casting;
        }

        if (content.Get<SkillDefinition>(request.SkillId) is not { Retired: false } skill)
        {
            return RpgSkillResultMessageComposer.UnknownSkill;
        }

        // A monster's skills are its content's rules, not a class's (spec M1c §4.2).
        if (caster.IsPlayer && !Knows(caster, skill))
        {
            return RpgSkillResultMessageComposer.NotLearned;
        }

        (int X, int Y)? cell = request.X >= 0 && request.Y >= 0 ? (request.X, request.Y) : null;

        if (Check(zone, caster, skill, request.TargetObjectId, cell) is { } code)
        {
            return code;
        }

        // A new cast replaces an approach under way.
        DropApproach(caster);
        PendingCast pending = new()
        {
            Skill = skill,
            TargetObjectId = request.TargetObjectId,
            Cell = cell,
        };

        if (Centre(zone, caster, pending) is { } centre && !InReach(zone, caster, skill, centre))
        {
            // A monster never approaches for a skill: its behavior places it (spec M1c §4.2).
            if (!caster.IsPlayer)
            {
                return InRange(zone, caster, skill, centre)
                    ? RpgSkillResultMessageComposer.NoLineOfSight
                    : RpgSkillResultMessageComposer.Unreachable;
            }

            // An approach would replace an instant already paid and waiting for its tick.
            if (caster.Get<PendingCast>() is { Ready: true })
            {
                return RpgSkillResultMessageComposer.Casting;
            }

            if (
                zone.Runtime.WalkNear(caster.ObjectId, centre.X, centre.Y, skill.Range)
                == WalkResult.NoPath
            )
            {
                return RpgSkillResultMessageComposer.Unreachable;
            }

            pending.Goal = centre;
            caster.Set(pending);

            return null;
        }

        return Begin(zone, caster, pending, zone.Runtime.NowMs);
    }

    /// <summary>Step 6 (cooldown, global cooldown, mana), then the start (spec §6.3).</summary>
    private string? Begin(CombatZone zone, Combatant caster, PendingCast pending, long now)
    {
        SkillDefinition skill = pending.Skill;
        SkillCooldowns cooldowns = Cooldowns(caster);

        if (cooldowns.Until.TryGetValue(skill.Id, out long until) && until > now)
        {
            return RpgSkillResultMessageComposer.Cooldown;
        }

        if (skill.GlobalCooldown && cooldowns.GlobalUntilMs > now)
        {
            return RpgSkillResultMessageComposer.GlobalCooldown;
        }

        if (caster.IsPlayer && caster.Mana < skill.Cost)
        {
            return RpgSkillResultMessageComposer.NoMana;
        }

        // An instant already paid and waiting for its tick is never replaced.
        if (caster.Get<PendingCast>() is { Ready: true })
        {
            return RpgSkillResultMessageComposer.Casting;
        }

        if (skill.GlobalCooldown)
        {
            cooldowns.GlobalUntilMs = now + _config.GlobalCooldownMs;
        }

        switch (skill.Type)
        {
            case SkillType.Instant:
                // Paid now, landing at the next tick: a second packet meanwhile is refused.
                Pay(zone, caster, skill, now);
                pending.Ready = true;
                caster.Set(pending);
                break;
            case SkillType.Cast:
                Start(zone, caster, pending, now + skill.CastMs, channel: false, now);
                break;
            case SkillType.Channel:
                Pay(zone, caster, skill, now);
                Start(zone, caster, pending, now + skill.ChannelMs, channel: true, now);
                break;
        }

        return null;
    }

    private void TickPending(CombatZone zone, Combatant c, PendingCast pending, long now)
    {
        SkillDefinition skill = pending.Skill;

        if (c.IsDead || c.IsGhost || c.Stunned)
        {
            c.Set<PendingCast>(null);

            return;
        }

        if (pending.Ready)
        {
            c.Set<PendingCast>(null);
            Land(zone, c, skill, pending.TargetObjectId, pending.Cell, now);
            SendCooldowns(zone, c, now);
            metrics.SkillCast(skill.Id, Resolved);

            return;
        }

        // The approach: the target may have died or left on the way.
        if (
            Check(zone, c, skill, pending.TargetObjectId, pending.Cell) is not null
            || Centre(zone, c, pending) is not { } centre
        )
        {
            c.Set<PendingCast>(null);
            Refuse(zone, c, skill, RpgSkillResultMessageComposer.Unreachable);

            return;
        }

        if (InReach(zone, c, skill, centre))
        {
            c.Set<PendingCast>(null);
            zone.Runtime.StopWalking(c.ObjectId);

            if (Begin(zone, c, pending, now) is { } refusal)
            {
                Refuse(zone, c, skill, refusal);
            }

            return;
        }

        if (pending.Goal != centre || !zone.Runtime.IsWalking(c.ObjectId))
        {
            if (
                zone.Runtime.WalkNear(c.ObjectId, centre.X, centre.Y, skill.Range)
                == WalkResult.NoPath
            )
            {
                c.Set<PendingCast>(null);
                Refuse(zone, c, skill, RpgSkillResultMessageComposer.Unreachable);

                return;
            }

            pending.Goal = centre;
        }
    }

    private void TickCast(CombatZone zone, Combatant c, CastState state, long now)
    {
        SkillDefinition skill = state.Skill;

        if (c.IsDead || c.IsGhost)
        {
            End(zone, c, RpgCastEnd.Cancelled);

            return;
        }

        if (c.Stunned)
        {
            End(zone, c, RpgCastEnd.Interrupted);

            return;
        }

        // One target: dead, gone or no longer fit, the cast stops (spec §6.4).
        if (
            SingleTarget(skill, state.TargetObjectId)
            && Check(zone, c, skill, state.TargetObjectId, state.Cell) is not null
        )
        {
            End(zone, c, RpgCastEnd.Cancelled);

            return;
        }

        if (state.Channel)
        {
            while (state.NextPulseAtMs <= now && state.NextPulseAtMs <= state.EndsAtMs && !c.IsDead)
            {
                state.NextPulseAtMs += skill.PulseMs;
                Land(zone, c, skill, state.TargetObjectId, state.Cell, now);
            }

            if (now >= state.EndsAtMs)
            {
                End(zone, c, RpgCastEnd.Done);
            }

            return;
        }

        if (now < state.EndsAtMs)
        {
            return;
        }

        // The end of a cast: still in reach, still the mana (spec §6.3).
        PendingCast target = new()
        {
            Skill = skill,
            TargetObjectId = state.TargetObjectId,
            Cell = state.Cell,
        };

        if (Centre(zone, c, target) is { } centre && !InReach(zone, c, skill, centre))
        {
            End(zone, c, RpgCastEnd.Failed);
            Refuse(
                zone,
                c,
                skill,
                InRange(zone, c, skill, centre)
                    ? RpgSkillResultMessageComposer.NoLineOfSight
                    : RpgSkillResultMessageComposer.Unreachable,
                count: false
            );

            return;
        }

        if (c.IsPlayer && c.Mana < skill.Cost)
        {
            End(zone, c, RpgCastEnd.Failed);
            Refuse(zone, c, skill, RpgSkillResultMessageComposer.NoMana, count: false);

            return;
        }

        // Ended before it lands: an effect that throws never leaves a cast to be paid again.
        Pay(zone, c, skill, now);
        End(zone, c, RpgCastEnd.Done);
        Land(zone, c, skill, state.TargetObjectId, state.Cell, now);
        SendCooldowns(zone, c, now);
    }

    private void Start(
        CombatZone zone,
        Combatant c,
        PendingCast pending,
        long endsAt,
        bool channel,
        long now
    )
    {
        c.Set(
            new CastState
            {
                Skill = pending.Skill,
                TargetObjectId = pending.TargetObjectId,
                Cell = pending.Cell,
                EndsAtMs = endsAt,
                NextPulseAtMs = now + pending.Skill.PulseMs,
                Channel = channel,
            }
        );
        c.Busy = true;

        // A cast that does not allow moving stops the walk under way (spec §6.4).
        if (!pending.Skill.MoveAllowed)
        {
            zone.Runtime.StopWalking(c.ObjectId);
        }
        zone.GetOrAdd<CastNews>()
            .Started(
                new RpgCastStart
                {
                    CasterObjectId = c.ObjectId,
                    SkillId = pending.Skill.Id,
                    DurationMs = (int)(endsAt - now),
                    Channel = channel,
                    TargetObjectId = pending.TargetObjectId,
                    X = pending.Cell?.X ?? -1,
                    Y = pending.Cell?.Y ?? -1,
                }
            );

        if (channel)
        {
            SendCooldowns(zone, c, now);
        }
    }

    /// <summary>The cast or channel stops: the zone is told why. An unpaid cast costs nothing.</summary>
    private void End(CombatZone zone, Combatant c, string reason)
    {
        if (c.Get<CastState>() is not { } state)
        {
            return;
        }

        c.Set<CastState>(null);
        c.Busy = false;
        zone.GetOrAdd<CastNews>()
            .Ended(
                new RpgCastEnd
                {
                    CasterObjectId = c.ObjectId,
                    SkillId = state.Skill.Id,
                    Reason = reason,
                }
            );
        metrics.SkillCast(state.Skill.Id, reason == RpgCastEnd.Done ? Resolved : reason);
    }

    /// <summary>The skill lands: its targets now, each effect on each (spec §6.3, §6.6).</summary>
    private void Land(
        CombatZone zone,
        Combatant caster,
        SkillDefinition skill,
        int targetObjectId,
        (int X, int Y)? cell,
        long now
    )
    {
        SkillContext ctx = new()
        {
            Zone = zone,
            Caster = caster,
            Skill = skill,
            Now = now,
            Hits = hits,
            Statuses = statuses,
            Config = _config,
            Content = content,
            Effects = effects,
        };

        ctx.Events.AddImpact(
            new RpgSkillImpact
            {
                CasterObjectId = caster.ObjectId,
                SkillId = skill.Id,
                TargetObjectId = targetObjectId,
                X = cell?.X ?? -1,
                Y = cell?.Y ?? -1,
            }
        );

        foreach (Combatant target in SkillTargets.Resolve(ctx, targetObjectId, cell))
        {
            effects.Apply(ctx, target);
        }
    }

    private void Pay(CombatZone zone, Combatant c, SkillDefinition skill, long now)
    {
        // A monster has no mana (spec M1c §4.2).
        if (c.IsPlayer)
        {
            c.Mana = Math.Max(0, c.Mana - skill.Cost);
            zone.VitalsChanged(c);
        }

        if (skill.CooldownMs > 0)
        {
            Cooldowns(c).Until[skill.Id] = now + skill.CooldownMs;
        }
    }

    private static void SendCooldowns(CombatZone zone, Combatant c, long now)
    {
        if (c.PlayerId is not { } player)
        {
            return;
        }

        SkillCooldowns cooldowns = Cooldowns(c);
        zone.SendTo(
            player,
            new RpgCooldownsMessageComposer
            {
                GlobalRemainingMs = (int)Math.Max(0, cooldowns.GlobalUntilMs - now),
                Skills =
                [
                    .. cooldowns
                        .Until.Where(kv => kv.Value > now)
                        .Select(kv => new RpgCooldown
                        {
                            SkillId = kv.Key,
                            RemainingMs = (int)(kv.Value - now),
                        }),
                ],
            }
        );
    }

    /// <summary>The player is told; counted unless the end of the cast already was (failed).</summary>
    private void Refuse(
        CombatZone zone,
        Combatant c,
        SkillDefinition skill,
        string code,
        bool count = true
    )
    {
        if (count)
        {
            metrics.SkillCast(skill.Id, code);
        }

        if (c.PlayerId is { } player)
        {
            zone.SendTo(
                player,
                new RpgSkillResultMessageComposer { SkillId = skill.Id, Code = code }
            );
        }
    }

    private bool Knows(Combatant c, SkillDefinition skill) =>
        KnownSkills.For(content, c.ClassId, c.SpecializationId, c.Level).Any(s => s.Id == skill.Id);

    private string? Check(
        CombatZone zone,
        Combatant caster,
        SkillDefinition skill,
        int targetObjectId,
        (int X, int Y)? cell
    ) =>
        TargetCheck.Validate(
            zone,
            skill,
            effects.IsOffensive(skill, content),
            caster,
            targetObjectId,
            cell
        );

    /// <summary>Where the skill must reach, or null when it is cast where the caster stands.</summary>
    private static (int X, int Y)? Centre(CombatZone zone, Combatant caster, PendingCast cast) =>
        cast.Skill.Target switch
        {
            SkillTarget.Self or SkillTarget.AroundCaster => null,
            SkillTarget.Ally when cast.TargetObjectId == 0 => null,
            SkillTarget.Ground => cast.Cell,
            _ => zone.Runtime.PositionOf(cast.TargetObjectId),
        };

    private static bool InReach(
        CombatZone zone,
        Combatant caster,
        SkillDefinition skill,
        (int X, int Y) centre
    ) =>
        InRange(zone, caster, skill, centre)
        && zone.Runtime.PositionOf(caster.ObjectId) is { } from
        && zone.Runtime.HasLineOfSight(from.X, from.Y, centre.X, centre.Y);

    private static bool InRange(
        CombatZone zone,
        Combatant caster,
        SkillDefinition skill,
        (int X, int Y) centre
    ) =>
        zone.Runtime.PositionOf(caster.ObjectId) is { } from
        && CombatMath.Chebyshev(from.X, from.Y, centre.X, centre.Y) <= skill.Range;

    private static bool SingleTarget(SkillDefinition skill, int targetObjectId) =>
        skill.Target is SkillTarget.Enemy or SkillTarget.AroundTarget
        || skill.Target == SkillTarget.Ally && targetObjectId != 0;

    private static void DropApproach(Combatant c)
    {
        if (c.Get<PendingCast>() is { Ready: false })
        {
            c.Set<PendingCast>(null);
        }

        Steer(c);
    }

    /// <summary>While a cast walks him to its target, the attack does not walk him to its own.</summary>
    private static void Steer(Combatant c) => c.Steered = c.Get<PendingCast>() is { Ready: false };

    private static SkillCooldowns Cooldowns(Combatant c)
    {
        if (c.Get<SkillCooldowns>() is not { } cooldowns)
        {
            cooldowns = new SkillCooldowns();
            c.Set(cooldowns);
        }

        return cooldowns;
    }
}
