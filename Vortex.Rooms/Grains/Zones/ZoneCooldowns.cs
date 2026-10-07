using System;
using System.Collections.Generic;
using System.Linq;
using Vortex.Primitives.Players;

namespace Vortex.Rooms.Grains.Zones;

/// <summary>
/// When a passage or a trigger works again, per player (plan D14) -- or for everyone, under player
/// 0, for what a clock set off. Kept by the zone rather than the visitor: stepping out of the room
/// and back must not reset a cooldown once a trigger can reward.
/// </summary>
public sealed class ZoneCooldowns
{
    /// <summary>
    /// Not a cap: once this many cooldowns are kept, the expired ones are dropped, and the next
    /// sweep waits until the map has doubled again -- each insert stays O(1) on average.
    /// </summary>
    private const int FirstSweepAt = 256;

    private readonly Dictionary<(PlayerId Player, string Key), long> _readyAtMs = [];
    private int _sweepAt = FirstSweepAt;

    public int Count => _readyAtMs.Count;

    public bool IsCooling(PlayerId player, string key, long nowMs) =>
        _readyAtMs.TryGetValue((player, key), out long readyAt) && readyAt > nowMs;

    public void Start(PlayerId player, string key, long readyAtMs, long nowMs)
    {
        if (_readyAtMs.Count >= _sweepAt)
        {
            foreach (
                (PlayerId, string) over in _readyAtMs
                    .Where(c => c.Value <= nowMs)
                    .Select(c => c.Key)
                    .ToList()
            )
            {
                _readyAtMs.Remove(over);
            }

            _sweepAt = Math.Max(FirstSweepAt, _readyAtMs.Count * 2);
        }

        _readyAtMs[(player, key)] = readyAtMs;
    }
}
