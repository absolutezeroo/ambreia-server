using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.World;

namespace Vortex.Rpg.Quests.Signals;

/// <summary>
/// The zone key of a room, as quests name zones (spec M2a §2.1): read once per room and kept.
/// ponytail: kept for the process; a zone republished under another key keeps the old one until restart.
/// </summary>
public sealed class ZoneKeys(IZoneRoomSource zones)
{
    private readonly ConcurrentDictionary<int, string?> _keys = new();

    public async Task<string?> KeyOfAsync(RoomId room)
    {
        if (_keys.TryGetValue(room.Value, out string? key))
        {
            return key;
        }

        key = (await zones.GetPublishedAsync(room, CancellationToken.None))?.Settings.Id;
        _keys[room.Value] = key;

        return key;
    }
}
