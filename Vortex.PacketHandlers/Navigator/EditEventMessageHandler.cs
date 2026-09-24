using System;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Messages.Registry;
using Vortex.PacketHandlers.Moderation;
using Vortex.Primitives.Moderation.WordFilter;
using Vortex.Primitives.Rooms;
using Vortex.Protocol.Messages.Incoming.Navigator;
using Vortex.Protocol.Messages.Outgoing.Navigator;

namespace Vortex.PacketHandlers.Navigator;

/// <summary>Edits the ad copy of a currently-active room advertisement -- see
/// CancelEventMessageHandler for the "Event" naming note.</summary>
public class EditEventMessageHandler(
    IRoomAdvertisementService roomAdvertisements,
    IWordFilter wordFilter
) : IMessageHandler<EditEventMessage>
{
    public async ValueTask HandleAsync(
        EditEventMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0 || message.Id <= 0 || string.IsNullOrWhiteSpace(message.Name))
        {
            return;
        }

        string? name = await wordFilter
            .PassAsync(ctx, message.Name, WordFilterSource.RoomEvent, ct)
            .ConfigureAwait(false);
        string? description = name is null
            ? null
            : await wordFilter
                .PassAsync(ctx, message.Description, WordFilterSource.RoomEvent, ct)
                .ConfigureAwait(false);

        if (name is null || description is null)
        {
            return;
        }

        RoomAdvertisementSnapshot? updated = await roomAdvertisements
            .EditAsync(message.Id, ctx.PlayerId, name, description, ct)
            .ConfigureAwait(false);

        if (updated is null)
        {
            return;
        }

        DateTime now = DateTime.UtcNow;

        await ctx.SendComposerAsync(
                new RoomEventMessageComposer
                {
                    AdvertisementId = updated.AdvertisementId,
                    OwnerPlayerId = updated.OwnerPlayerId,
                    OwnerName = updated.OwnerName,
                    RoomId = updated.RoomId,
                    Name = updated.Name,
                    Description = updated.Description,
                    MinutesSinceCreation = Math.Max(0, (int)(now - updated.CreatedAt).TotalMinutes),
                    MinutesRemaining = Math.Max(0, (int)(updated.ExpiresAt - now).TotalMinutes),
                    CategoryId = updated.CategoryId,
                },
                ct
            )
            .ConfigureAwait(false);
    }
}
