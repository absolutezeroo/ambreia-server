using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Protocol.Messages.Incoming.Navigator;
using Vortex.Protocol.Messages.Outgoing.Navigator;

namespace Vortex.PacketHandlers.Navigator;

public class RateFlatMessageHandler(IGrainFactory grainFactory) : IMessageHandler<RateFlatMessage>
{
    public async ValueTask HandleAsync(
        RateFlatMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0 || ctx.RoomId <= 0)
        {
            return;
        }

        IRoomSettings roomGrain = grainFactory.GetRoomSettings(ctx.RoomId);

        int? newScore = await roomGrain
            .RateRoomAsync(ctx.PlayerId, message.Points, ct)
            .ConfigureAwait(false);

        if (newScore is null)
        {
            return;
        }

        // The client never lowers its own like button: canRate comes from RoomRating and nowhere
        // else (RoomToolsWidgetHandler.rateRoom just sends and forgets). Without this the button
        // stays up for the rest of the visit and the score the player just moved never shows.
        await ctx.SendComposerAsync(
                new RoomRatingMessageComposer { Rating = newScore.Value, CanRate = false },
                ct
            )
            .ConfigureAwait(false);
    }
}
