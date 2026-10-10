using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rpg.Characters;

namespace Vortex.Rooms.Grains.Modules;

/// <summary>
/// A player's avatar in the zone's fights (spec M1a §4.4): on entry the zone reads his health and
/// mana once, and what its rules need of his character (spec Z1 §4.3), both at once; on exit it
/// gives health and mana back once. A fault there is logged and never blocks the entry or the exit
/// itself.
/// </summary>
public sealed partial class RoomAvatarModule
{
    private Task JoinFightsAsync(PlayerId player, int objectId, CancellationToken ct) =>
        Task.WhenAll(EnterFightsAsync(player, objectId, ct), ReadFactsAsync(player));

    private async Task EnterFightsAsync(PlayerId player, int objectId, CancellationToken ct)
    {
        try
        {
            await _roomGrain.Simulation.PlayerEnteredAsync(player, objectId, ct);
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

    private async Task ReadFactsAsync(PlayerId player)
    {
        if (_roomGrain._state.ZoneRuntime is null)
        {
            return;
        }

        try
        {
            CharacterZoneFacts? facts = await _roomGrain
                ._grainFactory.GetPlayerCharacterGrain(player)
                .GetZoneFactsAsync();

            // The zone may have been replaced, or the player gone, while the read was out.
            if (
                facts is not null
                && _roomGrain._state.ZoneRuntime is { } zone
                && _roomGrain._state.AvatarsByPlayerId.ContainsKey(player)
            )
            {
                zone.Facts[player] = facts;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _roomGrain._logger.LogWarning(
                ex,
                "The character of player {PlayerId} could not be read in room {RoomId}.",
                player.Value,
                _roomGrain.RoomId.Value
            );
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
