using FluentAssertions;
using Vortex.Rooms.Grains.Systems;
using Xunit;

namespace Vortex.Rooms.Tests.Pets;

/// <summary>
/// Whether a pet does as it is told. The breed, harvest and revive flags this class also covered
/// were removed in M0 with breeding and monsterplants; they now go out as constants.
/// </summary>
public sealed class PetActionFlagsTests
{
    /// <summary>
    /// The obedience rule, which a capture of the live hotel forced into existence: a pet there
    /// ignored four orders out of nine. The numbers are server configuration because they are
    /// assumed, so what is pinned here is the shape -- the bonus only ever counts levels ABOVE the
    /// requirement, the ceiling holds, and a base of 100 is the old always-obeys behaviour.
    /// </summary>
    [Theory]
    [InlineData(1, 1, 70)] // at the requirement: the base chance, no bonus
    [InlineData(8, 1, 95)] // seven levels over: the bonus is capped, not 105
    [InlineData(3, 1, 80)] // two levels over: +5 each
    [InlineData(1, 8, 70)] // under the requirement: never a penalty, the level gate handles that
    public void ObeyChance_AddsALevelBonusUnderItsCeiling(int level, int required, int expected) =>
        RoomPetRuntime.ObeyChancePercent(level, required, 70, 5, 95).Should().Be(expected);

    [Fact]
    public void ObeyChance_OfOneHundred_IsAlwaysObeyed() =>
        // Random.Shared.Next(100) can never reach 100, so this is the switch that turns refusing
        // off entirely -- the behaviour every hotel had before the roll existed.
        RoomPetRuntime.ObeyChancePercent(1, 1, 100, 0, 100).Should().Be(100);

    [Fact]
    public void ObeyChance_NeverLeavesTheRange() =>
        RoomPetRuntime.ObeyChancePercent(20, 1, 250, 50, 400).Should().Be(100);
}
