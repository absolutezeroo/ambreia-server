using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Vortex.Rooms.Grains;

public sealed partial class RoomGrain
{
    public Task<IReadOnlyList<string>> GetEnabledAchievementsAsync(CancellationToken ct) =>
        WiredSystem.GetEnabledAchievementsAsync(ct);
}
