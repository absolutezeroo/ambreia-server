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
/// Adds or removes one word from a room's chat filter, and sends the list back so the dialog
/// redraws. The dialog has no acknowledgement of its own — the refreshed list is the answer.
/// </summary>
public class UpdateRoomFilterMessageHandler(IGrainFactory grainFactory)
    : IMessageHandler<UpdateRoomFilterMessage>
{
    public async ValueTask HandleAsync(
        UpdateRoomFilterMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0 || message.RoomId <= 0)
        {
            return;
        }

        ImmutableArray<string> words = await grainFactory
            .GetRoomSettings(message.RoomId)
            .UpdateFilterWordAsync(ctx.PlayerId, message.IsAddingWord, message.Word, ct)
            .ConfigureAwait(false);

        await ctx.SendComposerAsync(new RoomFilterSettingsMessageComposer { BadWords = words }, ct)
            .ConfigureAwait(false);
    }
}
