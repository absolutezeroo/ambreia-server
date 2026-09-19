using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Badges.Snapshots;
using Vortex.Primitives.Orleans;
using Vortex.Protocol.Messages.Incoming.Inventory.Badges;
using Vortex.Protocol.Messages.Outgoing.Inventory.Badges;

namespace Vortex.PacketHandlers.Inventory.Badges;

public class GetBadgeInfoMessageHandler(IGrainFactory grainFactory)
    : IMessageHandler<GetBadgeInfoMessage>
{
    private readonly IGrainFactory _grainFactory = grainFactory;

    public async ValueTask HandleAsync(
        GetBadgeInfoMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0 || string.IsNullOrWhiteSpace(message.BadgeCode))
        {
            return;
        }

        BadgeInfoSnapshot info = await _grainFactory
            .GetBadgeDirectoryGrain()
            .GetInfoAsync(message.BadgeCode, ct)
            .ConfigureAwait(false);

        // The code is echoed from the request, not from the answer: the badge display widget
        // discards anything whose code is not the one it is waiting on, and a code it never asked
        // for would leave its plaque stuck on the previous badge.
        await ctx.SendComposerAsync(
                new BadgeInfoMessageComposer
                {
                    BadgeCode = message.BadgeCode,
                    OwnerCount = info.OwnerCount,
                    BadgeRarityId = (int)info.Rarity,
                },
                ct
            )
            .ConfigureAwait(false);
    }
}
