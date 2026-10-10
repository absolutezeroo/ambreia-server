using System.Threading.Tasks;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rpg.Characters;

namespace Vortex.Rooms.Grains;

/// <summary>What the zone knows of each player's character (spec Z1 §4.3).</summary>
public sealed partial class RoomGrain
{
    /// <summary>Server side only: the player's presence passes on what changed in their character.</summary>
    public Task UpdatePlayerFactsAsync(PlayerId player, CharacterZoneFacts facts)
    {
        // A relay that arrives after the player left must not bring them back.
        if (
            player.Value <= 0
            || _state.ZoneRuntime is not { } zone
            || !_state.AvatarsByPlayerId.ContainsKey(player)
        )
        {
            return Task.CompletedTask;
        }

        zone.Facts[player] = facts;

        return Task.CompletedTask;
    }
}
