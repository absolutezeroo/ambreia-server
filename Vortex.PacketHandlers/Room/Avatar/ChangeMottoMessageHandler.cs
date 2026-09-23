using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Moderation.WordFilter;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players.Grains;
using Vortex.Protocol.Messages.Incoming.Room.Avatar;
using Vortex.Protocol.Messages.Outgoing.Notifications;

namespace Vortex.PacketHandlers.Room.Avatar;

public class ChangeMottoMessageHandler(IGrainFactory grainFactory, IWordFilter wordFilter)
    : IMessageHandler<ChangeMottoMessage>
{
    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly IWordFilter _wordFilter = wordFilter;

    public async ValueTask HandleAsync(
        ChangeMottoMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId < 0)
        {
            return;
        }

        // A refused motto leaves the old one in place: the grain is never asked.
        WordFilterResult filtered = _wordFilter.Apply(
            message.Text,
            WordFilterSource.Motto,
            ctx.PlayerId
        );

        if (filtered.Refused)
        {
            await ctx.SendComposerAsync(WordFilterNotice.Create(_wordFilter.RefusedMessage), ct)
                .ConfigureAwait(false);

            return;
        }

        IPlayerGrain player = _grainFactory.GetPlayerGrain(ctx.PlayerId);

        await player.SetMottoAsync(filtered.Text, ct).ConfigureAwait(false);
    }
}
