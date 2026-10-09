using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rpg.Characters;

namespace Vortex.Rooms.Grains;

/// <summary>The zone's fights (spec M1a): the simulation does the work.</summary>
public sealed partial class RoomGrain
{
    /// <summary>Server side only: the player's presence passes on his character's new stats.</summary>
    public Task UpdateCombatantStatsAsync(
        PlayerId player,
        CharacterStatsSnapshot stats,
        CancellationToken ct
    )
    {
        // No client reaches this; an invalid player is still refused (actor guard).
        if (player == PlayerId.Invalid || player.Value <= 0)
        {
            return Task.CompletedTask;
        }

        Simulation.PlayerStatsChanged(player, stats);

        return Task.CompletedTask;
    }
}
