using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Vortex.Primitives.Rooms.Grains;

public partial interface IRoomWired
{
    /// <summary>
    /// The wired achievements this room's achievement-enabler add-ons allow, as the bare names the
    /// client lists (no <c>ACH_WF_</c>): what the "progress achievement" effect may advance here, and
    /// what <c>WiredEnvironment</c> tells the client to offer.
    /// </summary>
    public Task<IReadOnlyList<string>> GetEnabledAchievementsAsync(CancellationToken ct);
}
