using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using Microsoft.Extensions.Options;
using Vortex.PacketHandlers.Configuration;
using Vortex.Primitives.Networking;
using Vortex.Protocol.Messages.Incoming.Rpg.Characters;

namespace Vortex.PacketHandlers.Rpg;

/// <summary>
/// When each socket last passed each character-screen intent (spec F3a §5.5). A singleton: the
/// behavior that asks is built per packet.
/// </summary>
public sealed class RpgIntentLimiter(IOptions<RpgIntentLimitConfig> options)
{
    // Swept every this-many calls rather than on a timer, as TokenBucketRateLimiter does: a closed
    // socket's entries go once they are older than any interval.
    private const int SweepEveryNCalls = 1024;
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(5);

    private readonly RpgIntentLimitConfig _config = options.Value;
    private readonly ConcurrentDictionary<(SessionKey, Type), long> _lastPassed = new();
    private long _calls;

    public bool TryConsume(SessionKey session, IAccountPhaseMessage message)
    {
        TimeSpan interval = TimeSpan.FromMilliseconds(IntervalMs(message));
        long now = Stopwatch.GetTimestamp();
        bool passed = false;

        _lastPassed.AddOrUpdate(
            (session, message.GetType()),
            _ =>
            {
                passed = true;

                return now;
            },
            (_, last) =>
            {
                passed = Stopwatch.GetElapsedTime(last, now) >= interval;

                return passed ? now : last;
            }
        );

        if (Interlocked.Increment(ref _calls) % SweepEveryNCalls == 0)
        {
            Sweep(now);
        }

        return passed;
    }

    private int IntervalMs(IAccountPhaseMessage message) =>
        message switch
        {
            RpgCharacterCreateMessage => _config.CreateIntervalMs,
            RpgCharacterChooseClassMessage => _config.ChooseClassIntervalMs,
            RpgEnterWorldMessage => _config.EnterWorldIntervalMs,
            _ => _config.ListIntervalMs,
        };

    private void Sweep(long now)
    {
        foreach (((SessionKey, Type) key, long last) in _lastPassed)
        {
            if (Stopwatch.GetElapsedTime(last, now) > StaleAfter)
            {
                _lastPassed.TryRemove(key, out _);
            }
        }
    }
}
