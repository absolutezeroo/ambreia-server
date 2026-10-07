using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Groups;
using Vortex.Primitives.Groups.Snapshots;
using Vortex.Primitives.Orleans;
using Vortex.Protocol.Messages.Incoming.GroupForums;
using Vortex.Protocol.Messages.Outgoing.Groupforums;

namespace Vortex.PacketHandlers.GroupForums;

public class GetThreadMessageHandler(IGrainFactory grainFactory) : IMessageHandler<GetThreadMessage>
{
    private readonly IGrainFactory _grainFactory = grainFactory;

    public async ValueTask HandleAsync(
        GetThreadMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0 || message.GroupId <= 0)
        {
            return;
        }

        // The client's GetThreadMessageComposer carries no paging (groupId, threadId only), so this
        // is always the first page; the size comes from config rather than a literal, and the grain
        // clamps it against the configured maximum.
        int pageSize = await _grainFactory
            .GetServerConfigGrain()
            .GetIntAsync(
                GroupConfig.DefaultForumPageSizeKey,
                GroupConfig.DefaultForumPageSizeDefault
            )
            .ConfigureAwait(false);

        ThreadMessagesPageSnapshot? page = await _grainFactory
            .GetGroupForumGrain(message.GroupId)
            .GetMessagesAsync(ctx.PlayerId, message.ThreadId, 0, pageSize, ct)
            .ConfigureAwait(false);

        if (page is null)
        {
            return;
        }

        await ctx.SendComposerAsync(new ThreadMessagesMessageComposer { Page = page }, ct)
            .ConfigureAwait(false);
    }
}
