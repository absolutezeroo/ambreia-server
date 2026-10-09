using System.Linq;
using FluentAssertions;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Skills;
using Vortex.Rpg.Tests.Support;
using Xunit;

namespace Vortex.Rpg.Tests.Skills;

/// <summary>Spec M1b §9: the skills a character knows come from its class, its specialization and its
/// level — nothing is stored.</summary>
public sealed class KnownSkillsTests
{
    private static SkillDefinition Skill(
        string id,
        int level,
        string cls = "class.mage",
        params string[] specs
    ) =>
        new()
        {
            Id = id,
            Kind = "skill",
            Classes = [cls],
            Specializations = [.. specs],
            Level = level,
            Type = SkillType.Instant,
            Range = 5,
            Target = SkillTarget.Enemy,
            Effects = [new SkillEffectDefinition { Kind = "damage", Base = 1 }],
        };

    private static readonly TestContent Content = new(
        Skill("skill.mage.nova", 6),
        Skill("skill.mage.firebolt", 1),
        Skill("skill.mage.burst", 3),
        Skill("skill.mage.aaa", 3),
        Skill("skill.warrior.strike", 1, "class.warrior"),
        Skill("skill.mage.pyro", 1, "class.mage", "spec.mage.pyro"),
        Skill("skill.mage.old", 1) with
        {
            Retired = true,
        }
    );

    private static string[] Ids(string cls, string spec, int level) =>
        [.. KnownSkills.For(Content, cls, spec, level).Select(s => s.Id)];

    [Fact]
    public void ByLevel_ThenById() =>
        Ids("class.mage", "spec.mage.frost", 3)
            .Should()
            .Equal("skill.mage.firebolt", "skill.mage.aaa", "skill.mage.burst");

    [Fact]
    public void AnotherClass_KnowsNoneOfThem() =>
        Ids("class.warrior", "spec.warrior.arms", 20).Should().Equal("skill.warrior.strike");

    [Fact]
    public void ASpecializationSkill_IsKnownOnlyInIt() =>
        Ids("class.mage", "spec.mage.pyro", 1)
            .Should()
            .Equal("skill.mage.firebolt", "skill.mage.pyro");

    [Fact]
    public void ARetiredSkill_IsNeverKnown() =>
        Ids("class.mage", "spec.mage.frost", 20).Should().NotContain("skill.mage.old");
}
