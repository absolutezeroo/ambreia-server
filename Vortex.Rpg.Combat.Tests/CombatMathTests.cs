using FluentAssertions;
using Vortex.Rpg.Combat.Configuration;
using Xunit;

namespace Vortex.Rpg.Combat.Tests;

/// <summary>Spec M1a §6.1, §8.2: distance in tiles, attack cadence, XP by level gap.</summary>
public sealed class CombatMathTests
{
    private static readonly CombatConfig Config = new();

    [Theory]
    [InlineData(0, 0, 3, 1, 3)]
    [InlineData(5, 5, 4, 7, 2)]
    [InlineData(2, 2, 2, 2, 0)]
    public void Chebyshev_IsTheLargerAxis(int ax, int ay, int bx, int by, int expected) =>
        CombatMath.Chebyshev(ax, ay, bx, by).Should().Be(expected);

    [Fact]
    public void Xp_SameLevel_IsFull() => CombatMath.XpFor(100, 5, 5, Config).Should().Be(100);

    [Theory]
    [InlineData(6, 110)]
    [InlineData(8, 130)]
    [InlineData(20, 150)]
    public void Xp_PlusTenPercentPerLevelAbove_UpToFifty(int monsterLevel, int expected) =>
        CombatMath.XpFor(100, monsterLevel, 5, Config).Should().Be(expected);

    [Theory]
    [InlineData(4, 90)]
    [InlineData(1, 60)]
    public void Xp_MinusTenPercentPerLevelBelow(int monsterLevel, int expected) =>
        CombatMath.XpFor(100, monsterLevel, 5, Config).Should().Be(expected);

    [Theory]
    [InlineData(2, 10)]
    [InlineData(1, 12)]
    public void Xp_EightLevelsBelowOrMore_IsZero(int monsterLevel, int playerLevel) =>
        CombatMath.XpFor(100, monsterLevel, playerLevel, Config).Should().Be(0);

    [Theory]
    [InlineData(100, 2000)]
    [InlineData(200, 1000)]
    [InlineData(400, 1000)]
    [InlineData(50, 4000)]
    public void AttackInterval_FollowsSpeed_WithAFloor(int speedPercent, int expected) =>
        CombatMath.AttackIntervalMs(speedPercent, Config).Should().Be(expected);
}
