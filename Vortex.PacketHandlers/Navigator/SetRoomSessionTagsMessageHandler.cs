using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.PacketHandlers.Moderation;
using Vortex.Primitives.Moderation.WordFilter;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Protocol.Messages.Incoming.Navigator;

namespace Vortex.PacketHandlers.Navigator;

public class SetRoomSessionTagsMessageHandler(IGrainFactory grainFactory, IWordFilter wordFilter)
    : IMessageHandler<SetRoomSessionTagsMessage>
{
    public async ValueTask HandleAsync(
        SetRoomSessionTagsMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0 || ctx.RoomId <= 0)
        {
            return;
        }

        // RoomTag is strict: a tag is a navigator search key, and "bobba" spliced into one would
        // only list the room under a word nobody searches for. Either tag refused, neither is set.
        string? tag1 = message.Tag1;
        string? tag2 = message.Tag2;

        if (!string.IsNullOrEmpty(tag1))
        {
            tag1 = await wordFilter
                .PassAsync(ctx, tag1, WordFilterSource.RoomTag, ct)
                .ConfigureAwait(false);

            if (tag1 is null)
            {
                return;
            }
        }

        if (!string.IsNullOrEmpty(tag2))
        {
            tag2 = await wordFilter
                .PassAsync(ctx, tag2, WordFilterSource.RoomTag, ct)
                .ConfigureAwait(false);

            if (tag2 is null)
            {
                return;
            }
        }

        IRoomSettings roomGrain = grainFactory.GetRoomSettings(ctx.RoomId);

        await roomGrain.SetRoomTagsAsync(ctx.PlayerId, tag1, tag2, ct).ConfigureAwait(false);
    }
}
