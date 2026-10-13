using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Action;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rpg.Characters;

namespace Vortex.Rooms.Grains;

/// <summary>The zone's fights (spec M1a, M1b): the simulation does the work.</summary>
public sealed partial class RoomGrain
{
    /// <summary>Server side only: the player's presence passes on his character's new stats.</summary>
    public Task UpdateCombatantStatsAsync(
        PlayerId player,
        CharacterCombatProfile profile,
        CancellationToken ct
    )
    {
        // No client reaches this; an invalid player is still refused (actor guard).
        if (player == PlayerId.Invalid || player.Value <= 0)
        {
            return Task.CompletedTask;
        }

        Simulation.PlayerStatsChanged(player, profile);

        return Task.CompletedTask;
    }

    /// <summary>A cast from a player standing in this room (spec M1b §10.1); the zone answers.</summary>
    public Task CastSkillAsync(
        ActionContext ctx,
        string skillId,
        int targetObjectId,
        int x,
        int y,
        CancellationToken ct
    )
    {
        // Actor guard: a real player with an avatar in this room.
        if (ctx.PlayerId <= 0 || !_state.AvatarsByPlayerId.ContainsKey(ctx.PlayerId))
        {
            return Task.CompletedTask;
        }

        return Simulation.CastSkillAsync(ctx.PlayerId, skillId, targetObjectId, x, y, ct);
    }

    public Task CancelCastAsync(ActionContext ctx, CancellationToken ct)
    {
        // Actor guard: a real player with an avatar in this room.
        if (ctx.PlayerId <= 0 || !_state.AvatarsByPlayerId.ContainsKey(ctx.PlayerId))
        {
            return Task.CompletedTask;
        }

        Simulation.CancelCast(ctx.PlayerId);

        return Task.CompletedTask;
    }

    public Task SetAutoAttackAsync(ActionContext ctx, bool active, CancellationToken ct)
    {
        // Actor guard: a real player with an avatar in this room.
        if (ctx.PlayerId <= 0 || !_state.AvatarsByPlayerId.ContainsKey(ctx.PlayerId))
        {
            return Task.CompletedTask;
        }

        return Simulation.SetAutoAttackAsync(ctx.PlayerId, active, ct);
    }
}
