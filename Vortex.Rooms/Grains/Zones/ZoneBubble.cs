using Vortex.Logging.Extensions;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Protocol.Messages.Outgoing.Notifications;

namespace Vortex.Rooms.Grains.Zones;

/// <summary>A server message shown to one player: an area's name, a closed passage.</summary>
internal static class ZoneBubble
{
    public static void Show(RoomGrain room, PlayerId playerId, string kind, string message) =>
        room
            ._grainFactory.GetPlayerPresenceGrain(playerId)
            .SendComposerAsync(
                new NotificationDialogMessageComposer
                {
                    Type = kind,
                    Parameters = [new() { Key = "message", Value = message }],
                }
            )
            .LogAndForget(room._logger, "Zone bubble to player {PlayerId} failed", playerId.Value);
}
