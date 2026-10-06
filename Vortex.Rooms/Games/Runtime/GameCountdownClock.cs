using System;

namespace Vortex.Rooms.Games.Runtime;

/// <summary>
/// Pure countdown clock for the game timer furni. You set it to a number of seconds, start it, and
/// it counts down to zero — driving the counter furni's displayed value. Time is tracked in
/// milliseconds so it stays accurate regardless of the room tick rate; the displayed value is whole
/// seconds.
/// <para>
/// All state is ephemeral runtime state — never persisted, so a room reload resets the running
/// countdown.
/// </para>
/// </summary>
public sealed class GameCountdownClock
{
    private long _remainingMs;
    private long _resetToMs;
    private long _lastAdvanceMs;
    private bool _running;
    private bool _anchored;

    public bool IsRunning => _running;

    /// <summary>
    /// Whole seconds remaining, rounded up: shows the set value for its first full second and ticks
    /// down to 0 (e.g. 30 → … → 1 → 0).
    /// </summary>
    public int RemainingSeconds => (int)((_remainingMs + 999) / 1000);

    /// <summary>Set the remaining time to an absolute number of seconds ;
    /// this also becomes the value <see cref="Reset"/> restores.</summary>
    public void SetSeconds(int seconds)
    {
        _remainingMs = _resetToMs = Math.Max(0, seconds) * 1000L;
    }

    /// <summary>Add or subtract seconds, clamped at zero. Does not
    /// change the reset target.</summary>
    public void AddSeconds(int seconds)
    {
        _remainingMs = Math.Max(0, _remainingMs + seconds * 1000L);
    }

    /// <summary>Begin (or restart) counting down from the current value. The
    /// clock anchors to the room clock on its next <see cref="Advance"/>, so no time is needed here.</summary>
    public void Start()
    {
        _running = true;
        _anchored = false;
    }

    /// <summary>Halt, keeping the current remaining value.</summary>
    public void Halt()
    {
        _running = false;
    }

    /// <summary>Continue counting down from where it was paused.</summary>
    public void Resume()
    {
        if (_running)
        {
            return;
        }

        _running = true;
        _anchored = false;
    }

    /// <summary>Halt and restore the last set value.</summary>
    public void Reset()
    {
        _running = false;
        _remainingMs = _resetToMs;
    }

    /// <summary>
    /// Advance the countdown to <paramref name="nowMs"/>. The first advance after a start/resume only
    /// anchors to the room clock (no elapsed time counted). Returns the displayed second before and
    /// after (so callers can detect a display change or a threshold crossing) and whether the clock
    /// just reached zero (which also halts it).
    /// </summary>
    public CountdownTick Advance(long nowMs)
    {
        int before = RemainingSeconds;

        if (!_running)
        {
            return new CountdownTick(before, before, false);
        }

        if (!_anchored)
        {
            _lastAdvanceMs = nowMs;
            _anchored = true;

            return new CountdownTick(before, before, false);
        }

        long elapsed = nowMs - _lastAdvanceMs;
        _lastAdvanceMs = nowMs;

        if (elapsed > 0)
        {
            _remainingMs = Math.Max(0, _remainingMs - elapsed);
        }

        bool reachedZero = false;

        if (_remainingMs == 0)
        {
            _running = false;
            reachedZero = before > 0; // only report the transition into zero, once
        }

        return new CountdownTick(before, RemainingSeconds, reachedZero);
    }
}

/// <summary>Outcome of one <see cref="GameCountdownClock.Advance"/> step.</summary>
public readonly record struct CountdownTick(
    int PreviousSeconds,
    int RemainingSeconds,
    bool ReachedZero
)
{
    public bool DisplayChanged => PreviousSeconds != RemainingSeconds;

    /// <summary>Whether this step crossed the display down onto <paramref name="thresholdSeconds"/>
    /// (i.e. the counter just reached that time).</summary>
    public bool CrossedDownTo(int thresholdSeconds) =>
        PreviousSeconds > thresholdSeconds && RemainingSeconds <= thresholdSeconds;
}
