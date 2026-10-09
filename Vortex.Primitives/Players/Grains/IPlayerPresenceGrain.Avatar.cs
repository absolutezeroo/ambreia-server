using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Orleans.Snapshots.Players;
using Vortex.Primitives.Rpg.Characters;

namespace Vortex.Primitives.Players.Grains;

public partial interface IPlayerPresenceGrain
{
    public Task OnPlayerUpdatedAsync(PlayerSummarySnapshot snapshot, CancellationToken ct);
    public Task OnFigureUpdatedAsync(PlayerSummarySnapshot snapshot, CancellationToken ct);

    /// <summary>The character's stats changed (spec M1a §4.4): passed on to the zone room the
    /// player stands in, without waiting on it.</summary>
    public Task OnCharacterStatsChangedAsync(CharacterStatsSnapshot stats, CancellationToken ct);
}
