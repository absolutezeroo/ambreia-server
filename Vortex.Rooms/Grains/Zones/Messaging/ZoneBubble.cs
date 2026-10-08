using Vortex.Logging.Extensions;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Protocol.Messages.Outgoing.Notifications;

namespace Vortex.Rooms.Grains.Zones.Messaging;

/// <summary>A server message shown to one player (an area's name, a closed passage) or to the whole room.</summary>
internal static class ZoneBubble
{
    public static void Show(RoomGrain room, PlayerId playerId, string kind, string message) =>
        room
            ._grainFactory.GetPlayerPresenceGrain(playerId)
            .SendComposerAsync(Bubble(kind, message))
            .LogAndForget(room._logger, "Zone bubble to player {PlayerId} failed", playerId.Value);

    /// <summary>One message on the room's stream, whoever is in it: a clock's announcement.</summary>
    public static void ShowToRoom(RoomGrain room, string kind, string message) =>
        room.SendComposerToRoomAsync(Bubble(kind, message))
            .LogAndForget(room._logger, "Zone bubble to room {RoomId} failed", room.RoomId.Value);

    private static NotificationDialogMessageComposer Bubble(string kind, string message) =>
        new() { Type = kind, Parameters = [new() { Key = "message", Value = message }] };
}
