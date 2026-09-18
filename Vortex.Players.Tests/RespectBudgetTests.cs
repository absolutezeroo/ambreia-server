using System;
using FluentAssertions;
using Vortex.Players;
using Xunit;

namespace Vortex.Players.Tests;

public class RespectBudgetTests
{
    private static readonly DateTime Today = new(2026, 7, 19, 15, 0, 0);

    [Fact]
    public void TryConsume_UnderLimitSameDay_Allows()
    {
        (bool allowed, int given, DateTime reset) = RespectBudget.TryConsume(
            givenToday: 1,
            resetDate: Today,
            now: Today,
            dailyLimit: 3
        );

        allowed.Should().BeTrue();
        given.Should().Be(2);
        reset.Should().Be(Today.Date);
    }

    [Fact]
    public void TryConsume_AtLimitSameDay_Denies()
    {
        (bool allowed, int given, _) = RespectBudget.TryConsume(3, Today, Today, dailyLimit: 3);

        allowed.Should().BeFalse();
        given.Should().Be(3);
    }

    [Fact]
    public void TryConsume_NewDay_ResetsBudget()
    {
        // Used up yesterday, but it's a new day now.
        (bool allowed, int given, DateTime reset) = RespectBudget.TryConsume(
            givenToday: 3,
            resetDate: Today.AddDays(-1),
            now: Today,
            dailyLimit: 3
        );

        allowed.Should().BeTrue();
        given.Should().Be(1);
        reset.Should().Be(Today.Date);
    }

    [Fact]
    public void TryConsume_NoPriorResetDate_StartsFresh()
    {
        (bool allowed, int given, _) = RespectBudget.TryConsume(0, null, Today, dailyLimit: 3);

        allowed.Should().BeTrue();
        given.Should().Be(1);
    }

    /// <summary>
    /// The number the user object hands the client, which is the only thing that decides whether
    /// the respect button is drawn at all. A budget spent yesterday must read as full today, or the
    /// button stays hidden until the player gives a respect they cannot give.
    /// </summary>
    [Fact]
    public void Remaining_CountsDownWithinTheDay()
    {
        RespectBudget.Remaining(0, Today, Today, dailyLimit: 3).Should().Be(3);
        RespectBudget.Remaining(2, Today, Today, dailyLimit: 3).Should().Be(1);
        RespectBudget.Remaining(3, Today, Today, dailyLimit: 3).Should().Be(0);
    }

    [Fact]
    public void Remaining_IsFullAgainOnANewDay()
    {
        RespectBudget
            .Remaining(3, Today.AddDays(-1), Today, dailyLimit: 3)
            .Should()
            .Be(3, "yesterday's spending is not today's");
    }

    [Fact]
    public void Remaining_NeverGoesNegativeWhenTheLimitIsLowered()
    {
        // An operator can drop respect.daily_limit while players hold counts above it.
        RespectBudget.Remaining(5, Today, Today, dailyLimit: 3).Should().Be(0);
    }

    [Fact]
    public void Remaining_IsZeroWhenTheAllowanceIsOff()
    {
        // How the replenish button stays hidden: respect.daily_replenishes defaults to 0.
        RespectBudget.Remaining(0, Today, Today, dailyLimit: 0).Should().Be(0);
    }
}
