using FluentAssertions;
using Vortex.Rooms.Games.Runtime;
using Xunit;

namespace Vortex.Rooms.Tests.Games;

/// <summary>
/// Locks the game timer's countdown: set-to-N, count down while running, whole-second display,
/// pause/resume/reset, and the once-only zero + threshold crossings. Start/Resume anchor to the room clock on their first
/// <see cref="GameCountdownClock.Advance"/>, so each test takes an anchoring advance first.
/// </summary>
public sealed class GameCountdownClockTests
{
    [Fact]
    public void SetSeconds_ShowsThatValue_AndDoesNotRunUntilStarted()
    {
        GameCountdownClock clock = new();
        clock.SetSeconds(30);

        clock.RemainingSeconds.Should().Be(30);
        clock.IsRunning.Should().BeFalse();

        // Advancing a stopped clock changes nothing.
        clock.Advance(10_000).DisplayChanged.Should().BeFalse();
        clock.RemainingSeconds.Should().Be(30);
    }

    [Fact]
    public void CountsDown_WhileRunning_WithCeilingDisplay()
    {
        GameCountdownClock clock = new();
        clock.SetSeconds(30);
        clock.Start();
        clock.Advance(1_000); // anchor

        // Half a second in, the display still shows 30 (ceil).
        clock.Advance(1_500).RemainingSeconds.Should().Be(30);
        // A full second in, it ticks to 29.
        clock.Advance(2_000).RemainingSeconds.Should().Be(29);
        // Ten seconds from the anchor → 20.
        clock.Advance(11_000).RemainingSeconds.Should().Be(20);
    }

    [Fact]
    public void ReachesZero_Once_AndHalts()
    {
        GameCountdownClock clock = new();
        clock.SetSeconds(2);
        clock.Start();
        clock.Advance(0); // anchor

        clock.Advance(1_000).ReachedZero.Should().BeFalse();

        CountdownTick atZero = clock.Advance(2_000);
        atZero.RemainingSeconds.Should().Be(0);
        atZero.ReachedZero.Should().BeTrue();
        clock.IsRunning.Should().BeFalse();

        // Past zero it stays at zero and never re-reports the transition.
        clock.Advance(5_000).ReachedZero.Should().BeFalse();
    }

    [Fact]
    public void Pause_HoldsValue_Resume_Continues()
    {
        GameCountdownClock clock = new();
        clock.SetSeconds(30);
        clock.Start();
        clock.Advance(0); // anchor
        clock.Advance(5_000).RemainingSeconds.Should().Be(25);

        clock.Halt();
        // Time passes while halted — no change.
        clock.Advance(60_000).RemainingSeconds.Should().Be(25);

        // Resume re-anchors and picks up from 25, not from wall-clock elapsed.
        clock.Resume();
        clock.Advance(60_000); // re-anchor
        clock.Advance(63_000).RemainingSeconds.Should().Be(22);
    }

    [Fact]
    public void Reset_RestoresLastSetValue_AndHalts()
    {
        GameCountdownClock clock = new();
        clock.SetSeconds(30);
        clock.Start();
        clock.Advance(0); // anchor
        clock.Advance(20_000).RemainingSeconds.Should().Be(10);

        clock.Reset();

        clock.IsRunning.Should().BeFalse();
        clock.RemainingSeconds.Should().Be(30);
    }

    [Fact]
    public void AddSeconds_AdjustsRemaining_ClampedAtZero()
    {
        GameCountdownClock clock = new();
        clock.SetSeconds(10);

        clock.AddSeconds(5);
        clock.RemainingSeconds.Should().Be(15);

        clock.AddSeconds(-100);
        clock.RemainingSeconds.Should().Be(0);
    }

    [Fact]
    public void CrossedDownTo_FiresExactlyOnce_OnThreshold()
    {
        GameCountdownClock clock = new();
        clock.SetSeconds(30);
        clock.Start();
        clock.Advance(0); // anchor

        // From 30 down to 11 — has not crossed the 10s threshold yet.
        clock.Advance(19_000).CrossedDownTo(10).Should().BeFalse();
        // 30-19=11 → 30-21=9 passes through 10.
        clock.Advance(21_000).CrossedDownTo(10).Should().BeTrue();
        // Already below — does not fire again.
        clock.Advance(25_000).CrossedDownTo(10).Should().BeFalse();
    }
}
