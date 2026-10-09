using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rpg.Characters;

namespace Vortex.Rpg.Zones;

/// <summary>
/// What a zone runs for its fights (spec M1a §4.3): one per zone activation, created by
/// <see cref="IZoneSimulationFactory"/>. The zone calls it; it acts back through
/// <see cref="IZoneRuntime"/>.
/// </summary>
public interface IZoneSimulation
{
    /// <summary>The player's avatar is in the zone: his health and mana are read once.</summary>
    public Task PlayerEnteredAsync(PlayerId player, int objectId, CancellationToken ct);

    /// <summary>The player leaves the zone: his health and mana are given back once — awaited, so
    /// the next zone reads what this one wrote.</summary>
    public Task PlayerLeftAsync(PlayerId player, CancellationToken ct);

    /// <summary>True when <paramref name="objectId"/> is a monster: the simulation answered the
    /// player (target, attack started, or a refusal). False leaves it to the zone.</summary>
    public Task<bool> SelectTargetAsync(PlayerId player, int objectId, CancellationToken ct);

    /// <summary>The player cleared his target or walked on his own: his attack stops.</summary>
    public void StopAttack(PlayerId player);

    public Task TickAsync(CancellationToken ct);

    /// <summary>For the zone's entry burst: every combatant's health; null when there is nothing.</summary>
    public IComposer? EntryVitals();

    /// <summary>The player's stats changed in the zone (an item, a level): only the stats are taken,
    /// health is bounded to the new maximum.</summary>
    public void PlayerStatsChanged(PlayerId player, CharacterStatsSnapshot stats);
}
