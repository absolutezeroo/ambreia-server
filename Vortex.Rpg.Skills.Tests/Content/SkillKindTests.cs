using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Content.Kinds;
using Vortex.Rpg.Content.Validation;
using Vortex.Rpg.Skills.Effects;
using Xunit;

namespace Vortex.Rpg.Skills.Tests.Content;

/// <summary>The rules of a skill (spec M1b §5.1, §5.4): one test per rule, each on a valid skill with
/// one field broken. The effect rules are the real ones the skills module declares.</summary>
public sealed class SkillKindTests
{
    private static readonly ClassDefinition Mage = new()
    {
        Id = "class.mage",
        Kind = "class",
        Role = ClassRole.Damage,
        Range = 5,
        PrimaryStat = PrimaryStat.Intelligence,
        Base = new StatBlock
        {
            Strength = 4,
            Intelligence = 14,
            Agility = 5,
            Health = 60,
            Mana = 70,
        },
        PerLevel = new StatBlock(),
    };

    private static readonly StatusDefinition Burning = new()
    {
        Id = "status.burning",
        Kind = "status",
        Type = StatusType.Dot,
        DurationMs = 6000,
        PeriodMs = 2000,
        Stacking = StatusStacking.Stack,
        MaxStacks = 3,
        Coefficient = 0.2,
    };

    private static readonly SkillDefinition Firebolt = new()
    {
        Id = "skill.mage.firebolt",
        Kind = "skill",
        Classes = ["class.mage"],
        Level = 1,
        Cost = 10,
        Type = SkillType.Cast,
        CastMs = 2000,
        Range = 5,
        Target = SkillTarget.Enemy,
        Effects =
        [
            new SkillEffectDefinition
            {
                Kind = "damage",
                Base = 8,
                Coefficient = 1.8,
            },
        ],
    };

    private static SkillKind Kind() =>
        new([
            new DamageEffect(),
            new HealEffect(),
            new ApplyStatusEffect(),
            new InterruptEffect(),
            new DispelEffect(),
            new ThreatEffect(),
            new ResurrectEffect(),
        ]);

    private static string[] Codes(SkillDefinition skill) =>
        [
            .. Kind()
                .Check(
                    skill,
                    new ContentCheck(
                        new Dictionary<string, ContentDefinition>
                        {
                            [skill.Id] = skill,
                            [Mage.Id] = Mage,
                            [Burning.Id] = Burning,
                            ["spec.mage.fire"] = new SpecializationDefinition
                            {
                                Id = "spec.mage.fire",
                                Kind = "specialization",
                                Class = "class.mage",
                            },
                        },
                        _ => false
                    )
                )
                .Select(f => f.Code),
        ];

    private static SkillDefinition With(params SkillEffectDefinition[] effects) =>
        Firebolt with
        {
            Effects = [.. effects],
        };

    [Fact]
    public void AValidSkill_HasNoFinding() => Codes(Firebolt).Should().BeEmpty();

    [Fact]
    public void AnUndeclaredEffect_IsRefused() =>
        Codes(With(new SkillEffectDefinition { Kind = "push" })).Should().Equal("UnknownEffect");

    [Fact]
    public void ADamageEffect_WithoutBaseNorCoefficient_IsRefused() =>
        Codes(With(new SkillEffectDefinition { Kind = "damage" })).Should().Equal("BadEffect");

    [Fact]
    public void AHealEffect_WithoutBaseNorCoefficient_IsRefused() =>
        Codes(With(new SkillEffectDefinition { Kind = "heal" })).Should().Equal("BadEffect");

    [Fact]
    public void AnApplyStatus_WithoutAStatus_IsRefused() =>
        Codes(With(new SkillEffectDefinition { Kind = "apply_status" }))
            .Should()
            .Equal("BadEffect");

    [Fact]
    public void AnApplyStatus_OfAnUnknownStatus_IsRefused() =>
        Codes(With(new SkillEffectDefinition { Kind = "apply_status", Status = "status.nope" }))
            .Should()
            .Equal("BrokenReference");

    [Fact]
    public void AKnownStatus_IsAccepted() =>
        Codes(With(new SkillEffectDefinition { Kind = "apply_status", Status = Burning.Id }))
            .Should()
            .BeEmpty();

    [Fact]
    public void ADispel_OfNothing_IsRefused() =>
        Codes(With(new SkillEffectDefinition { Kind = "dispel" })).Should().Equal("BadEffect");

    [Fact]
    public void AThreatEffect_WithoutAPercent_IsRefused() =>
        Codes(With(new SkillEffectDefinition { Kind = "threat" })).Should().Equal("BadEffect");

    [Fact]
    public void AnInterrupt_NeedsNothing() =>
        Codes(With(new SkillEffectDefinition { Kind = "interrupt" })).Should().BeEmpty();

    [Fact]
    public void AnUnknownClass_IsRefused() =>
        Codes(Firebolt with { Classes = ["class.bard"] }).Should().Equal("BrokenReference");

    [Fact]
    public void NoClass_IsAMonsterSkill() =>
        Codes(Firebolt with { Classes = [] }).Should().BeEmpty();

    [Theory]
    [InlineData(TelegraphShape.Circle, 3, 1, 90)]
    [InlineData(TelegraphShape.Line, 5, 3, 90)]
    [InlineData(TelegraphShape.Cone, 10, 5, 180)]
    public void AValidTelegraph_HasNoFinding(
        TelegraphShape shape,
        int size,
        int width,
        int angle
    ) => Codes(Telegraphed(shape, size, width, angle)).Should().BeEmpty();

    [Theory]
    [InlineData(0, 1, 90)]
    [InlineData(11, 1, 90)]
    [InlineData(3, 2, 90)]
    [InlineData(3, 7, 90)]
    [InlineData(3, 1, 20)]
    [InlineData(3, 1, 190)]
    public void ATelegraphOutOfBounds_IsRefused(int size, int width, int angle) =>
        Codes(Telegraphed(TelegraphShape.Cone, size, width, angle)).Should().Equal("BadTelegraph");

    [Fact]
    public void AnInstantTelegraph_IsRefused() =>
        Codes(
                Telegraphed(TelegraphShape.Circle, 3, 1, 90) with
                {
                    Type = SkillType.Instant,
                    CastMs = 0,
                }
            )
            .Should()
            .Equal("TelegraphWithoutCast");

    [Fact]
    public void AChannelTelegraph_IsRefused() =>
        Codes(
                Telegraphed(TelegraphShape.Circle, 3, 1, 90) with
                {
                    Type = SkillType.Channel,
                    ChannelMs = 3000,
                    PulseMs = 1000,
                }
            )
            .Should()
            .Equal("TelegraphWithoutCast");

    [Theory]
    [InlineData(TelegraphShape.Line, SkillTarget.Self)]
    [InlineData(TelegraphShape.Cone, SkillTarget.AroundCaster)]
    public void ALineOrConeWithoutAim_IsRefused(TelegraphShape shape, SkillTarget target) =>
        Codes(Telegraphed(shape, 3, 1, 90) with { Target = target, Radius = 2, Range = 0 })
            .Should()
            .Equal("TelegraphNeedsAim");

    [Fact]
    public void ACircleAroundTheCaster_HasNoFinding() =>
        Codes(
                Telegraphed(TelegraphShape.Circle, 3, 1, 90) with
                {
                    Target = SkillTarget.AroundCaster,
                    Radius = 3,
                    Range = 0,
                }
            )
            .Should()
            .BeEmpty();

    private static SkillDefinition Telegraphed(
        TelegraphShape shape,
        int size,
        int width,
        int angle
    ) =>
        Firebolt with
        {
            Telegraph = new SkillTelegraph
            {
                Shape = shape,
                Size = size,
                Width = width,
                Angle = angle,
            },
        };

    [Fact]
    public void ACast_WithoutItsTime_IsRefused() =>
        Codes(Firebolt with { CastMs = 0 }).Should().Equal("CastWithoutTime");

    [Fact]
    public void AChannel_WithoutAPulse_IsRefused() =>
        Codes(Firebolt with { Type = SkillType.Channel, CastMs = 0, ChannelMs = 3000 })
            .Should()
            .Equal("ChannelWithoutPulse");

    [Fact]
    public void AChannel_PulsingSlowerThanItLasts_IsRefused() =>
        Codes(
                Firebolt with
                {
                    Type = SkillType.Channel,
                    CastMs = 0,
                    ChannelMs = 3000,
                    PulseMs = 4000,
                }
            )
            .Should()
            .Equal("ChannelWithoutPulse");

    [Theory]
    [InlineData(SkillTarget.Ground)]
    [InlineData(SkillTarget.AroundCaster)]
    [InlineData(SkillTarget.AroundTarget)]
    public void AnArea_WithoutARadius_IsRefused(SkillTarget target) =>
        Codes(
                Firebolt with
                {
                    Target = target,
                    Range = target == SkillTarget.AroundCaster ? 0 : Firebolt.Range,
                }
            )
            .Should()
            .Equal("AreaWithoutRadius");

    [Fact]
    public void NoEffect_IsRefused() =>
        Codes(Firebolt with { Effects = [] }).Should().Equal("NoEffect");

    [Fact]
    public void ANegativeValue_IsRefused() =>
        Codes(Firebolt with { Cost = -1 }).Should().Equal("NegativeValue");

    [Fact]
    public void ALevelBelowOne_IsRefused() =>
        Codes(Firebolt with { Level = 0 }).Should().Equal("BadLevel");

    /// <summary>"effects":[null] parses to a list holding null: a finding, not a crash.</summary>
    [Fact]
    public void ANullEffect_IsRefused() =>
        Codes(Firebolt with { Effects = [null!] }).Should().Equal("NullEntry");

    [Fact]
    public void AnUnknownSpecialization_IsRefused() =>
        Codes(Firebolt with { Specializations = ["spec.mage.pyr"] })
            .Should()
            .Equal("BrokenReference");

    [Fact]
    public void AKnownSpecialization_IsAccepted() =>
        Codes(Firebolt with { Specializations = ["spec.mage.fire"] }).Should().BeEmpty();

    [Theory]
    [InlineData(SkillTarget.Self)]
    [InlineData(SkillTarget.AroundCaster)]
    public void ARange_OnASkillCastOnOneself_IsRefused(SkillTarget target) =>
        Codes(Firebolt with { Target = target, Radius = 2 }).Should().Equal("RangeOnSelf");
}
