using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rpg.Characters;

namespace Vortex.Primitives.Rooms.Grains;

/// <summary>A zone room's fights, as the server sees them (spec M1a §4.4).</summary>
[Alias("Vortex.Primitives.Rooms.Grains.IRoomCombat")]
public interface IRoomCombat : IGrainWithIntegerKey
{
    /// <summary>The player's character changed its stats while he is here (an item, a level): his
    /// maximum follows, his health is bounded. Server side only, through his presence.</summary>
    public Task UpdateCombatantStatsAsync(
        PlayerId player,
        CharacterStatsSnapshot stats,
        CancellationToken ct
    );
}
