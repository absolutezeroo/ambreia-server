using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vortex.Primitives.Players;

namespace Vortex.Rooms.Grains.Modules;

/// <summary>
/// A player's avatar in the zone's fights (spec M1a §4.4): on entry the zone reads his health and
/// mana once, on exit it gives them back once. A fault there is logged and never blocks the entry
/// or the exit itself.
/// </summary>
public sealed partial class RoomAvatarModule
{
    private async Task JoinFightsAsync(PlayerId player, int objectId, CancellationToken ct)
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
