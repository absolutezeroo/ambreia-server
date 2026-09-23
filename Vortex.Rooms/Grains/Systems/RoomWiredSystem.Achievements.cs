using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Rooms.Object.Furniture;
using Vortex.Rooms.Object.Logic.Furniture.Floor.Wired.Addons;

namespace Vortex.Rooms.Grains.Systems;

public sealed partial class RoomWiredSystem
{
    /// <summary>
    /// Every achievement the room's enabler add-ons allow, in the order they were first named.
    /// </summary>
    /// <remarks>
    /// A walk over the room's items, not an index: it is asked on room entry and when an enabler is
    /// saved, never per tick, and a room holds one or two enablers at most.
    /// </remarks>
    public async Task<IReadOnlyList<string>> GetEnabledAchievementsAsync(CancellationToken ct)
    {
        List<string> enabled = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (IRoomItem item in Room.AllItems())
        {
            if (item.Logic is not WiredAddonAchievementEnabler enabler)
            {
                continue;
            }

            foreach (string name in await enabler.GetEnabledAchievementsAsync(ct))
            {
                if (seen.Add(name))
                {
                    enabled.Add(name);
                }
            }
        }

        return enabled;
    }
}
