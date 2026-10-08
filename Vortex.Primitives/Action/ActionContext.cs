using System;
using Orleans;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Avatars;

namespace Vortex.Primitives.Action;

[GenerateSerializer, Immutable]
public readonly record struct ActionContext(
    ActionOrigin Origin,
    SessionKey SessionKey,
    PlayerId PlayerId,
    RoomId RoomId
)
{
    public static ActionContext System { get; } =
        new(ActionOrigin.System, SessionKey.Invalid, PlayerId.Invalid, RoomId.Invalid);

    public static ActionContext CreateForSystem(RoomId roomId) =>
        new(ActionOrigin.System, SessionKey.Invalid, PlayerId.Invalid, roomId);

    public static ActionContext CreateForPlayer(PlayerId playerId, RoomId roomId) =>
        new(ActionOrigin.Player, SessionKey.Invalid, playerId, roomId);

    public static ActionContext CreateForObjectContext(IRoomObjectContext ctx) =>
        ctx switch
        {
            IRoomPlayerContext playerCtx => CreateForPlayer(
                playerCtx.RoomObject.PlayerId,
                ctx.RoomId
            ),
            // Specific type (CA2201): callers can distinguish "this context kind is not supported"
            // from an arbitrary failure, and a bare Exception cannot be caught selectively.
            _ => throw new NotSupportedException(
                $"Cannot create an ActionContext for room object context '{ctx.GetType().Name}'."
            ),
        };

    /// <summary>
    /// Nobody, acting on nothing. What an event that was not caused by anyone carries, and it holds
    /// <see cref="ActionOrigin.None"/> rather than <see cref="ActionOrigin.System"/>: a context
    /// named Invalid should not be the most privileged one in the process.
    /// </summary>
    public static ActionContext Invalid =>
        new(ActionOrigin.None, SessionKey.Invalid, PlayerId.Invalid, RoomId.Invalid);
}
