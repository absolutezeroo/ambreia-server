using System;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Badges;
using Vortex.Primitives.Badges.Snapshots;
using Vortex.Primitives.Orleans;
using Vortex.Protocol.Messages.Incoming.Users;
using Vortex.Protocol.Messages.Outgoing.Users;

namespace Vortex.PacketHandlers.Users;

public class GetBadgeLeaderboardMessageHandler(IGrainFactory grainFactory)
    : IMessageHandler<GetBadgeLeaderboardMessage>
{
    private readonly IGrainFactory _grainFactory = grainFactory;

    public async ValueTask HandleAsync(
        GetBadgeLeaderboardMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0)
        {
            return;
        }

        // The board type is the only field worth refusing on: the grain bounds the chunk itself,
        // and an unknown type there would score nobody and answer an empty board, which reads as
        // "this board is empty" rather than "that request was nonsense".
        if (!Enum.IsDefined(typeof(BadgeLeaderboardType), message.Type))
        {
            return;
        }

        BadgeLeaderboardPageSnapshot page = await _grainFactory
            .GetBadgeDirectoryGrain()
            .GetLeaderboardAsync(
                (BadgeLeaderboardType)message.Type,
                message.Rarity,
                message.ChunkIndex,
                message.ChunkSize,
                (int)ctx.PlayerId,
                ct
            )
            .ConfigureAwait(false);

        await ctx.SendComposerAsync(new BadgeLeaderboardResultMessageComposer { Page = page }, ct)
            .ConfigureAwait(false);
    }
}
