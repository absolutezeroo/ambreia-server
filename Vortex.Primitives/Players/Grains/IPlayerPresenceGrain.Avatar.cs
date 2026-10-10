using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Orleans.Snapshots.Players;
using Vortex.Primitives.Rpg.Characters;

namespace Vortex.Primitives.Players.Grains;

public partial interface IPlayerPresenceGrain
{
    public Task OnPlayerUpdatedAsync(PlayerSummarySnapshot snapshot, CancellationToken ct);

    public Task OnFigureUpdatedAsync(PlayerSummarySnapshot snapshot, CancellationToken ct);

    /// <summary>The character's stats, level or class changed (spec M1a §4.4): passed on to the
    /// zone room the player stands in, without waiting on it.</summary>
    public Task OnCharacterStatsChangedAsync(CharacterCombatProfile profile, CancellationToken ct);

    /// <summary>The character's level or what it owns changed (spec Z1 §4.3): passed on to the
    /// zone room the player stands in, without waiting on it.</summary>
    public Task OnCharacterFactsChangedAsync(CharacterZoneFacts facts, CancellationToken ct);
}
