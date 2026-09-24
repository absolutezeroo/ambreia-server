using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.PacketHandlers.Moderation;
using Vortex.Primitives.Moderation.WordFilter;
using Vortex.Primitives.Orleans;
using Vortex.Protocol.Messages.Incoming.Users;
using Vortex.Protocol.Messages.Outgoing.Users;

namespace Vortex.PacketHandlers.Users;

public class UpdateGuildIdentityMessageHandler(IGrainFactory grainFactory, IWordFilter wordFilter)
    : IMessageHandler<UpdateGuildIdentityMessage>
{
    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly IWordFilter _wordFilter = wordFilter;

    public async ValueTask HandleAsync(
        UpdateGuildIdentityMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0 || message.GroupId <= 0)
        {
            return;
        }

        string? name = await _wordFilter
            .PassAsync(ctx, message.Name, WordFilterSource.Group, ct)
            .ConfigureAwait(false);
        string? description = name is null
            ? null
            : await _wordFilter
                .PassAsync(ctx, message.Description, WordFilterSource.Group, ct)
                .ConfigureAwait(false);

        if (name is null || description is null)
        {
            // Reason 1 is the client's "Invalid Group name"; 0, which the failure below sends,
            // reads "Room is already a homeroom" and would say something untrue here.
            await ctx.SendComposerAsync(new GuildEditFailedMessageComposer { Reason = 1 }, ct)
                .ConfigureAwait(false);

            return;
        }

        bool ok = await _grainFactory
            .GetGroupGrain(message.GroupId)
            .UpdateIdentityAsync(ctx.PlayerId, name, description, ct)
            .ConfigureAwait(false);

        if (ok)
        {
            await ctx.SendComposerAsync(
                    new GroupDetailsChangedMessageComposer { GroupId = message.GroupId },
                    ct
                )
                .ConfigureAwait(false);
        }
        else
        {
            await ctx.SendComposerAsync(new GuildEditFailedMessageComposer { Reason = 0 }, ct)
                .ConfigureAwait(false);
        }
    }
}
