using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.PacketHandlers.Moderation;
using Vortex.Primitives.Action;
using Vortex.Primitives.Moderation.WordFilter;
using Vortex.Primitives.Orleans;
using Vortex.Protocol.Messages.Incoming.Userdefinedroomevents.Wiredtrading;
using Vortex.Protocol.Messages.Outgoing.Userdefinedroomevents.Wiredtrading;

namespace Vortex.PacketHandlers.UserDefinedRoomEvents.Wiredtrading;

/// <summary>
/// The chest's settings dialog, saved.
/// </summary>
public class SaveWiredChestSettingsMessageHandler(
    IGrainFactory grainFactory,
    IWordFilter wordFilter
) : IMessageHandler<SaveWiredChestSettingsMessage>
{
    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly IWordFilter _wordFilter = wordFilter;

    public async ValueTask HandleAsync(
        SaveWiredChestSettingsMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx is null || ctx.PlayerId <= 0 || ctx.RoomId <= 0)
        {
            return;
        }

        // A refusal skips the success reply below, which is what closes the dialog: it stays open
        // on what the owner typed, and the bubble says which part to change.
        string? name = await _wordFilter
            .PassAsync(ctx, message.Name, WordFilterSource.Wired, ct)
            .ConfigureAwait(false);
        string? description = name is null
            ? null
            : await _wordFilter
                .PassAsync(ctx, message.Description, WordFilterSource.Wired, ct)
                .ConfigureAwait(false);

        if (name is null || description is null)
        {
            return;
        }

        await _grainFactory
            .GetRoomWired(ctx.RoomId)
            .SaveWiredChestSettingsAsync(
                ActionContext.CreateForPlayer(ctx.PlayerId, ctx.RoomId),
                message.ChestId,
                name,
                description,
                message.EveryoneCanOpen,
                message.EveryoneCanDonate,
                message.ChestState,
                message.PreviewItems,
                message.PreviewAmount,
                ct
            )
            .ConfigureAwait(false);

        // The screen closes on this, not on the save itself — without it the dialog sits there
        // after a save that worked.
        await ctx.SendComposerAsync(
                new WiredChestUpdateSuccessMessageComposer
                {
                    ChestId = message.ChestId,
                    IsNotificationPreferences = false,
                },
                ct
            )
            .ConfigureAwait(false);
    }
}
