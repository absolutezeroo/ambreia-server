using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.PacketHandlers.Moderation;
using Vortex.Primitives.Moderation.WordFilter;
using Vortex.Primitives.Orleans;
using Vortex.Protocol.Messages.Incoming.Room.Engine;

namespace Vortex.PacketHandlers.Room.Engine;

/// <summary>
/// Writes a sticky note. The room grain owns the merge into one legacy string and the broadcast, so
/// this is the same path the spam-wall note already takes.
/// </summary>
public class SetItemDataMessageHandler(IGrainFactory grainFactory, IWordFilter wordFilter)
    : IMessageHandler<SetItemDataMessage>
{
    public async ValueTask HandleAsync(
        SetItemDataMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0 || ctx.RoomId <= 0)
        {
            return;
        }

        // The text only: the colour is a hex code, never prose.
        string? text = await wordFilter
            .PassAsync(ctx, message.Text, WordFilterSource.PostIt, ct)
            .ConfigureAwait(false);

        if (text is null)
        {
            return;
        }

        await grainFactory
            .GetRoomFurni(ctx.RoomId)
            .SetPostItAsync(ctx.AsActionContext(), message.ItemId, message.ColorHex, text, ct)
            .ConfigureAwait(false);
    }
}
