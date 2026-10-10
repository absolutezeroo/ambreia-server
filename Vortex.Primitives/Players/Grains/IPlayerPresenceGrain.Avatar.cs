using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Orleans.Snapshots.Players;
using Vortex.Primitives.Rpg.Characters;
using Vortex.Primitives.World.Rules;

namespace Vortex.Primitives.Players.Grains;

public partial interface IPlayerPresenceGrain
{
    public Task OnPlayerUpdatedAsync(PlayerSummarySnapshot snapshot, CancellationToken ct);

    public Task OnFigureUpdatedAsync(PlayerSummarySnapshot snapshot, CancellationToken ct);

    /// <summary>The character's stats, level or class changed (spec M1a §4.4): passed on to the
    /// zone room the player stands in, without waiting on it.</summary>
    public Task OnCharacterStatsChangedAsync(CharacterCombatProfile profile, CancellationToken ct);

    /// <summary>A part of what a zone knows of the player changed (spec Z1 §4.3): passed on to the
    /// zone room the player stands in, without waiting on it.</summary>
    public Task OnZoneFactsChangedAsync(ZoneFacts facts, CancellationToken ct);
}
