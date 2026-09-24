using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.PacketHandlers.Moderation;
using Vortex.Primitives.Groups.Snapshots;
using Vortex.Primitives.Moderation.WordFilter;
using Vortex.Primitives.Orleans;
using Vortex.Protocol.Messages.Incoming.GroupForums;
using Vortex.Protocol.Messages.Outgoing.Groupforums;

namespace Vortex.PacketHandlers.GroupForums;

public class PostMessageMessageHandler(IGrainFactory grainFactory, IWordFilter wordFilter)
    : IMessageHandler<PostMessageMessage>
{
    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly IWordFilter _wordFilter = wordFilter;

    public async ValueTask HandleAsync(
        PostMessageMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0 || message.GroupId <= 0)
        {
            return;
        }

        // The title only on a new thread: a reply's is ignored by GroupForumGrain.PostAsync, and
        // refusing a reply for a field nobody reads would be refusing it for nothing.
        string? title = message.Title;

        if (message.ThreadId == 0)
        {
            title = await _wordFilter
                .PassAsync(ctx, message.Title, WordFilterSource.Forum, ct)
                .ConfigureAwait(false);

            if (title is null)
            {
                return;
            }
        }

        string? body = await _wordFilter
            .PassAsync(ctx, message.Message, WordFilterSource.Forum, ct)
            .ConfigureAwait(false);

        if (body is null)
        {
            return;
        }

        ForumPostResultSnapshot? result = await _grainFactory
            .GetGroupForumGrain(message.GroupId)
            .PostAsync(ctx.PlayerId, message.ThreadId, title, body, ct)
            .ConfigureAwait(false);

        if (result is null)
        {
            return;
        }

        if (result.IsNewThread && result.Thread is not null)
        {
            await ctx.SendComposerAsync(
                    new PostThreadMessageComposer
                    {
                        GroupId = result.GroupId,
                        Thread = result.Thread,
                    },
                    ct
                )
                .ConfigureAwait(false);
        }
        else if (result.Post is not null)
        {
            await ctx.SendComposerAsync(
                    new PostMessageMessageComposer
                    {
                        GroupId = result.GroupId,
                        ThreadId = result.ThreadId,
                        Post = result.Post,
                    },
                    ct
                )
                .ConfigureAwait(false);
        }
    }
}
