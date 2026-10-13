using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rpg.Characters;

namespace Vortex.Rpg.Zones;

/// <summary>A zone that runs no fight: a room with no zone document, or a host without the combat
/// module. Everything is left to the zone.</summary>
public sealed class NoZoneSimulation : IZoneSimulation
{
    public static readonly NoZoneSimulation Instance = new();

    public Task PlayerEnteredAsync(PlayerId player, int objectId, CancellationToken ct) =>
        Task.CompletedTask;

    public Task PlayerLeftAsync(PlayerId player, CancellationToken ct) => Task.CompletedTask;

    public Task<bool> SelectTargetAsync(PlayerId player, int objectId, CancellationToken ct) =>
        Task.FromResult(false);

    public Task SetAutoAttackAsync(PlayerId player, bool active, CancellationToken ct) =>
        Task.CompletedTask;

    public void StopAttack(PlayerId player) { }

    public bool IsTargetable(int objectId) => true;

    public Task TickAsync(CancellationToken ct) => Task.CompletedTask;

    public IEnumerable<IComposer> EntryVitals(PlayerId player) => [];

    public void PlayerStatsChanged(PlayerId player, CharacterCombatProfile profile) { }

    public Task CastSkillAsync(
        PlayerId player,
        string skillId,
        int targetObjectId,
        int x,
        int y,
        CancellationToken ct
    ) => Task.CompletedTask;

    public void CancelCast(PlayerId player) { }

    public void DeathChoice(PlayerId player, string choice) { }

    public void PlayerWalked(PlayerId player) { }
}
