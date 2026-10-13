using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Primitives.World.Rules;

namespace Vortex.Rooms.Grains.Modules;

/// <summary>
/// A player's avatar in the zone's fights (spec M1a §4.4): on entry the zone reads his health and
/// mana once, on exit it gives them back once. A fault there is logged and never blocks the entry
/// or the exit itself. And what the zone's rules know of him (spec Z1 §4.3), kept from the entry.
/// </summary>
public sealed partial class RoomAvatarModule
{
    private async Task JoinFightsAsync(PlayerId player, int objectId, CancellationToken ct)
    {
        try
        {
            await _roomGrain.Simulation.PlayerEnteredAsync(player, objectId, ct);

            // A room with no zone runs no fight: the character tells the HUD itself.
            if (_roomGrain._state.Zone is null)
            {
                await _roomGrain._grainFactory.GetPlayerCharacterGrain(player).PushVitalsAsync();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _roomGrain._logger.LogWarning(
                ex,
                "Player {PlayerId} could not join the fights of room {RoomId}.",
                player.Value,
                _roomGrain.RoomId.Value
            );
        }
    }

    /// <summary>The parts read on entry, each replacing what an earlier visit left.</summary>
    private void KeepFacts(PlayerId player, ZoneFacts[] facts)
    {
        if (_roomGrain._state.ZoneRuntime is not { } zone || facts.Length == 0)
        {
            return;
        }

        Dictionary<Type, ZoneFacts> parts = RoomGrain.PartsOf(zone.Facts, player);

        foreach (ZoneFacts part in facts)
        {
            parts[part.GetType()] = part;
        }
    }

    private async Task LeaveFightsAsync(PlayerId player, CancellationToken ct)
    {
        try
        {
            await _roomGrain.Simulation.PlayerLeftAsync(player, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _roomGrain._logger.LogWarning(
                ex,
                "Player {PlayerId} could not leave the fights of room {RoomId}.",
                player.Value,
                _roomGrain.RoomId.Value
            );
        }
    }
}
