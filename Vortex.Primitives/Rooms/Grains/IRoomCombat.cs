using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Primitives.Action;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rpg.Characters;

namespace Vortex.Primitives.Rooms.Grains;

/// <summary>A zone room's fights, as the server sees them (spec M1a §4.4).</summary>
[Alias("Vortex.Primitives.Rooms.Grains.IRoomCombat")]
public interface IRoomCombat : IGrainWithIntegerKey
{
    /// <summary>The player casts a skill (spec M1b §10.1): at a room object (0: none) or a cell
    /// (−1: none). Refused or under way, the zone tells him.</summary>
    public Task CastSkillAsync(
        ActionContext ctx,
        string skillId,
        int targetObjectId,
        int x,
        int y,
        CancellationToken ct
    );

    /// <summary>The player stops his own cast.</summary>
    public Task CancelCastAsync(ActionContext ctx, CancellationToken ct);

    /// <summary>The player's character changed while he is here (an item, a level, a class): his
    /// stats, level, range and threat follow, his health is bounded — the profile's own health and
    /// mana are not read. Server side only, through his presence.</summary>
    public Task UpdateCombatantStatsAsync(
        PlayerId player,
        CharacterCombatProfile profile,
        CancellationToken ct
    );
}
