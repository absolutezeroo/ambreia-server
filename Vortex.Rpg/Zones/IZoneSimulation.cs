using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rpg.Characters;

namespace Vortex.Rpg.Zones;

/// <summary>
/// What a zone runs for its fights (spec M1a §4.3): one per zone activation, created by
/// <see cref="IZoneSimulationFactory"/>. The zone calls it; it acts back through
/// <see cref="IZoneRuntime"/>, and publishes its events (a monster killed, a player dead) on the
/// event bus it gets from DI. It lives and dies with the zone's activation: nothing to tear down,
/// the monsters are the zone's avatars. With no player in the zone it does nothing (INV-003); the
/// first one in wakes it.
/// </summary>
public interface IZoneSimulation
{
    /// <summary>The player's avatar is in the zone: his health and mana are read once.</summary>
    public Task PlayerEnteredAsync(PlayerId player, int objectId, CancellationToken ct);

    /// <summary>The player leaves the zone: his health and mana are given back once — awaited, so
    /// the next zone reads what this one wrote.</summary>
    public Task PlayerLeftAsync(PlayerId player, CancellationToken ct);

    /// <summary>La cible choisie par le joueur (0 : aucune), retenue pour la menace et l'attaque
    /// auto (M1d §2.3). Vrai quand c'est un monstre : la simulation a répondu (RpgTarget). Faux
    /// laisse la zone répondre.</summary>
    public Task<bool> SelectTargetAsync(PlayerId player, int objectId, CancellationToken ct);

    /// <summary>Démarre ou arrête l'attaque auto sur la cible choisie ; un refus est dit au joueur.</summary>
    public Task SetAutoAttackAsync(PlayerId player, bool active, CancellationToken ct);

    /// <summary>The player walked on his own: his attack stops.</summary>
    public void StopAttack(PlayerId player);

    /// <summary>Faux pour un joueur KO : nul ne le cible, sauf une compétence faite pour (M1d §2.3).</summary>
    public bool IsTargetable(int objectId);

    public Task TickAsync(CancellationToken ct);

    /// <summary>For the zone's entry burst of <paramref name="player"/>: every combatant's health,
    /// and his own vitals when he already fights here (back within the grace period).</summary>
    public IEnumerable<IComposer> EntryVitals(PlayerId player);

    /// <summary>The player's character changed in the zone (an item, a level, a class): its stats,
    /// level, range and threat are taken; its health and mana never are — in a zone the zone's own
    /// are the truth, bounded to the new maximum.</summary>
    public void PlayerStatsChanged(PlayerId player, CharacterCombatProfile profile);

    /// <summary>The player casts a skill (spec M1b §6): refused at once (the player is told why), or
    /// under way.</summary>
    public Task CastSkillAsync(
        PlayerId player,
        string skillId,
        int targetObjectId,
        int x,
        int y,
        CancellationToken ct
    );

    /// <summary>The player stops his own cast.</summary>
    public void CancelCast(PlayerId player);

    /// <summary>Le choix d'un joueur KO : wait, release, accept, decline (M1d §2.3).</summary>
    public void DeathChoice(PlayerId player, string choice);

    /// <summary>The player walked on his own: a cast that does not allow it ends (spec M1b §6.4).</summary>
    public void PlayerWalked(PlayerId player);
}
