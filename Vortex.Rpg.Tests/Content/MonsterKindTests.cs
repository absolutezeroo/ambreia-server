using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Content.Kinds;
using Vortex.Rpg.Content.Validation;
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

    private static string[] Codes(MonsterDefinition wolf) =>
        [
            .. new MonsterKind()
                .Check(
                    wolf,
                    new ContentCheck(
                        new Dictionary<string, ContentDefinition> { [wolf.Id] = wolf },
                        _ => false
                    )
                )
                .Select(f => f.Code),
        ];

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

    [Fact]
    public void NoHealth_IsRefused() =>
        Codes(Wolf with { Base = Wolf.Base with { Health = 0 } }).Should().Equal("NoHealth");
}
