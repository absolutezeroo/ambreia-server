using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Protocol.Messages.Incoming.RoomSettings;
using Vortex.Protocol.Messages.Outgoing.Room.Chat;

namespace Vortex.PacketHandlers.RoomSettings;

/// <summary>
/// Opens the filter tab of the room settings dialog. The tab draws itself from this answer alone,
/// so a room whose owner never asks gets no list at all.
/// </summary>
public class GetCustomRoomFilterMessageHandler(IGrainFactory grainFactory)
    : IMessageHandler<GetCustomRoomFilterMessage>
{
    public async ValueTask HandleAsync(
        GetCustomRoomFilterMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0 || message.RoomId <= 0)
        {
            return;
        }

        // The packet names an arbitrary room id, so the ownership check is the grain's, not this
        // handler's -- same shape as the rights and ban lists next door.
        ImmutableArray<string> words = await grainFactory
            .GetRoomSettings(message.RoomId)
            .GetFilterWordsAsync(ctx.PlayerId, ct)
            .ConfigureAwait(false);

        await ctx.SendComposerAsync(new RoomFilterSettingsMessageComposer { BadWords = words }, ct)
            .ConfigureAwait(false);
    }
}
