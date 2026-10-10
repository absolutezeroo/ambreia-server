using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Vortex.Primitives.Rpg.Content;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Content.Kinds;
using Vortex.Rpg.Content.Validation;
using Vortex.Rpg.Tests.Support;
using Xunit;

namespace Vortex.Rpg.Tests.Content;

/// <summary>The rules of a monster (spec M1a §5.1): one test per rule, each on a valid wolf with one
/// field broken.</summary>
public sealed class MonsterKindTests
{
    private static readonly MonsterDefinition Wolf = new()
    {
        Id = "monster.forest.wolf",
        Kind = "monster",
        Figure = "0 3 C0A060",
        Aggressive = true,
        Base = new MonsterStatBlock
        {
            Health = 40,
            Armor = 2,
            DamageMin = 3,
            DamageMax = 5,
            CritPercent = 5,
            DodgePercent = 3,
        },
        PerLevel = new MonsterStatBlock
        {
            Health = 12,
            Armor = 1,
            DamageMin = 1,
            DamageMax = 2,
        },
        Xp = 20,
        XpPerLevel = 6,
    };

    private static readonly SkillDefinition Bite = new()
    {
        Id = "skill.monster.bite",
        Kind = "skill",
        Classes = [],
        Level = 1,
        Type = SkillType.Instant,
        Range = 3,
        Target = SkillTarget.Enemy,
        Effects = [new SkillEffectDefinition { Kind = "damage", Base = 4 }],
    };

    private static readonly StatusDefinition Enraged = new()
    {
        Id = "status.enraged",
        Kind = "status",
        Type = StatusType.Buff,
        DurationMs = 1000,
    };

    private static readonly ProgressionDefinition Progression = new()
    {
        Id = "progression.default",
        Kind = "progression",
        MaxLevel = 20,
        XpToNext = [.. Enumerable.Repeat(100, 19)],
        HealthPerStrength = 5,
        ManaPerIntelligence = 5,
        CritBasePercent = 5,
        CritPerAgilityPercent = 0.1,
        CritCapPercent = 50,
        DodgePerAgilityPercent = 0.1,
        DodgeCapPercent = 30,
    };

    private static IEnumerable<ContentFinding> Findings(MonsterDefinition wolf)
    {
        Dictionary<string, ContentDefinition> all = new()
        {
            [wolf.Id] = wolf,
            [Bite.Id] = Bite,
            [Enraged.Id] = Enraged,
            [Progression.Id] = Progression,
            [Boss.Id] = Boss,
        };

        return new MonsterKind([
            new BehaviorKey("melee"),
            new BehaviorKey("ranged"),
            new BehaviorKey("caster"),
        ]).Check(wolf, new ContentCheck(all, _ => false));
    }

    private static string[] Codes(MonsterDefinition wolf) =>
        [.. Findings(wolf).Where(f => f.Severity == ContentSeverity.Error).Select(f => f.Code)];

    private static readonly MonsterDefinition Boss = Wolf with
    {
        Id = "monster.grove.guardian",
        Rank = MonsterRank.Boss,
    };

    private static MonsterSkillRule Rule(
        int minRange = 0,
        int below = 100,
        int chance = 100,
        string skill = "skill.monster.bite"
    ) =>
        new()
        {
            Skill = skill,
            MinRange = minRange,
            BelowHealthPercent = below,
            Chance = chance,
        };

    private static MonsterDefinition WithPhase(MonsterPhase phase) =>
        Wolf with
        {
            Phases = [phase],
        };

    [Fact]
    public void AnUndeclaredBehavior_IsRefused() =>
        Codes(Wolf with { Behavior = "flying" }).Should().Equal("UnknownBehavior");

    [Theory]
    [InlineData("ranged")]
    [InlineData("caster")]
    public void ADeclaredBehavior_HasNoFinding(string behavior) =>
        Codes(Wolf with { Behavior = behavior }).Should().BeEmpty();

    [Fact]
    public void AValidRule_HasNoFinding() =>
        Codes(Wolf with { Skills = [Rule(minRange: 3, below: 50, chance: 30)] }).Should().BeEmpty();

    [Fact]
    public void ARuleToAnUnknownSkill_IsRefused() =>
        Codes(Wolf with { Skills = [Rule(skill: "skill.monster.nope")] })
            .Should()
            .Equal("BrokenReference");

    [Theory]
    [InlineData(-1, 100, 100)]
    [InlineData(4, 100, 100)]
    [InlineData(0, 0, 100)]
    [InlineData(0, 101, 100)]
    [InlineData(0, 100, 0)]
    [InlineData(0, 100, 101)]
    public void ARuleOutOfBounds_IsRefused(int minRange, int below, int chance) =>
        Codes(Wolf with { Skills = [Rule(minRange, below, chance)] }).Should().Equal("BadRule");

    [Fact]
    public void AValidPhase_HasNoFinding() =>
        Codes(
                WithPhase(
                    new MonsterPhase
                    {
                        AtHealthPercent = 66,
                        Skills = [Rule()],
                        Status = Enraged.Id,
                        Summon = new MonsterSummon { Monster = Wolf.Id, Count = 8 },
                        Yell = "monster.forest.wolf.phase2",
                    }
                )
            )
            .Should()
            .BeEmpty();

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void APhaseThresholdOutOfOneToNinetyNine_IsRefused(int percent) =>
        Codes(WithPhase(new MonsterPhase { AtHealthPercent = percent })).Should().Equal("BadPhase");

    [Fact]
    public void TwoPhasesAtTheSameThreshold_AreRefused() =>
        Codes(
                Wolf with
                {
                    Phases =
                    [
                        new MonsterPhase { AtHealthPercent = 50 },
                        new MonsterPhase { AtHealthPercent = 50 },
                    ],
                }
            )
            .Should()
            .Equal("BadPhase");

    [Theory]
    [InlineData(0, 0)]
    [InlineData(9, 0)]
    [InlineData(1, 21)]
    public void ASummonOutOfBounds_IsRefused(int count, int level) =>
        Codes(
                WithPhase(
                    new MonsterPhase
                    {
                        AtHealthPercent = 50,
                        Summon = new MonsterSummon
                        {
                            Monster = Wolf.Id,
                            Count = count,
                            Level = level,
                        },
                    }
                )
            )
            .Should()
            .Equal("BadPhase");

    [Fact]
    public void AnUnknownPhaseStatusOrSummon_IsRefused() =>
        Codes(
                WithPhase(
                    new MonsterPhase
                    {
                        AtHealthPercent = 50,
                        Status = "status.nope",
                        Summon = new MonsterSummon { Monster = "monster.nope" },
                    }
                )
            )
            .Should()
            .Equal("BrokenReference", "BrokenReference");

    [Fact]
    public void ABossSummon_IsRefused() =>
        Codes(
                WithPhase(
                    new MonsterPhase
                    {
                        AtHealthPercent = 50,
                        Summon = new MonsterSummon { Monster = Boss.Id },
                    }
                )
            )
            .Should()
            .Equal("BossSummon");

    [Fact]
    public void AnEliteWithoutSkills_IsAWarning() =>
        Findings(Wolf with { Rank = MonsterRank.Elite })
            .Should()
            .ContainSingle(f => f.Code == "NoSkills" && f.Severity == ContentSeverity.Warning);

    [Fact]
    public void AValidWolf_HasNoFinding() => Codes(Wolf).Should().BeEmpty();

    [Fact]
    public void ANegativeValue_IsRefused() =>
        Codes(Wolf with { Base = Wolf.Base with { Armor = -1 } }).Should().Equal("NegativeValue");

    [Fact]
    public void ANegativeXp_IsRefused() =>
        Codes(Wolf with { Xp = -1 }).Should().Equal("NegativeValue");

    [Fact]
    public void MinAboveMax_IsRefused() =>
        Codes(Wolf with { Base = Wolf.Base with { DamageMin = 9 } }).Should().Equal("DamageRange");

    [Fact]
    public void MinAboveMaxPerLevel_IsRefused() =>
        Codes(Wolf with { PerLevel = Wolf.PerLevel with { DamageMin = 3 } })
            .Should()
            .Equal("DamageRange");

    [Fact]
    public void ARangeBelowOne_IsRefused() =>
        Codes(Wolf with { AttackRange = 0 }).Should().Equal("BadRange");

    [Fact]
    public void AnIntervalBelowHalfASecond_IsRefused() =>
        Codes(Wolf with { AttackIntervalMs = 499 }).Should().Equal("BadInterval");

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void APercentOutOfRange_IsRefused(double percent) =>
        Codes(Wolf with { Base = Wolf.Base with { CritPercent = percent } })
            .Should()
            .Contain("BadPercent");

    [Theory]
    [InlineData("")]
    [InlineData("0 3")]
    [InlineData("dog 3 C0A060")]
    [InlineData("0 x C0A060")]
    public void AFigureThatIsNotTypeRaceColour_IsRefused(string figure) =>
        Codes(Wolf with { Figure = figure }).Should().Equal("BadFigure");

    [Theory]
    [InlineData(-1)]
    [InlineData(11)]
    public void ACorpseDelayOutOfZeroToTen_IsRefused(int seconds) =>
        Codes(Wolf with { CorpseSeconds = seconds }).Should().Equal("BadCorpse");

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public void ACorpseDelayOfZeroToTen_IsAllowed(int seconds) =>
        Codes(Wolf with { CorpseSeconds = seconds }).Should().BeEmpty();

    [Fact]
    public void NoHealth_IsRefused() =>
        Codes(Wolf with { Base = Wolf.Base with { Health = 0 } }).Should().Equal("NoHealth");
}
