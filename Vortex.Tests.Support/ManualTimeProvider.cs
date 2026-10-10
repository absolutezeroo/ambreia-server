using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Vortex.Tests.Support;

/// <summary>
/// A clock the test moves by hand, timers included: <see cref="Advance" /> fires every timer whose
/// due time has passed, in due order, on the calling thread. For code that arms a deadline and must
/// be proven to act (or not) when it elapses.
/// </summary>
public sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
{
    private readonly List<ManualTimer> _timers = [];
    private readonly Lock _sync = new();

    public DateTimeOffset Now { get; private set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;

    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period
    )
    {
        ManualTimer timer = new(this, callback, state);
        timer.Change(dueTime, period);

        return timer;
    }

    /// <summary>Moves the clock forward and fires what came due, in order.</summary>
    public void Advance(TimeSpan by)
    {
        DateTimeOffset target = Now + by;

        while (true)
        {
            ManualTimer? next;

            lock (_sync)
            {
                next = _timers.Where(t => t.DueAt <= target).OrderBy(t => t.DueAt).FirstOrDefault();
            }

            if (next is null)
            {
                break;
            }

            Now = next.DueAt!.Value;
            next.Fire();
        }

        Now = target;
    }

    private sealed class ManualTimer(
        ManualTimeProvider owner,
        TimerCallback callback,
        object? state
    ) : ITimer
    {
        private TimeSpan _period;

        public DateTimeOffset? DueAt { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner._sync)
            {
                _period = period;
                DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : owner.Now + dueTime;

                if (DueAt is not null && !owner._timers.Contains(this))
                {
                    owner._timers.Add(this);
                }
                else if (DueAt is null)
                {
                    owner._timers.Remove(this);
                }
            }

            return true;
        }

        public void Fire()
        {
            lock (owner._sync)
            {
                DueAt =
                    _period == Timeout.InfiniteTimeSpan || _period == TimeSpan.Zero
                        ? null
                        : DueAt!.Value + _period;

                if (DueAt is null)
                {
                    owner._timers.Remove(this);
                }
            }

            callback(state);
        }

        public void Dispose()
        {
            lock (owner._sync)
            {
                DueAt = null;
                owner._timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();

            return ValueTask.CompletedTask;
        }
    }
}
