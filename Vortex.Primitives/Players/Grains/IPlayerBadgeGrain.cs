using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Primitives.Players.Snapshots;

namespace Vortex.Primitives.Players.Grains;

public interface IPlayerBadgeGrain : IGrainWithIntegerKey
{
    public Task<ImmutableArray<PlayerBadgeSnapshot>> GetBadgesAsync(CancellationToken ct);

    /// <summary>Applies the five display slots the client just sent and answers with the badges
    /// that ended up worn — the requested codes the player actually owns, ordered by slot. The
    /// caller has to echo that set back over the wire, so handing it over here saves reading the
    /// same rows again one grain call later.</summary>
    public Task<ImmutableArray<PlayerBadgeSnapshot>> SetActivatedBadgesAsync(
        List<(int SlotId, string BadgeCode)> slots,
        CancellationToken ct
    );
}
