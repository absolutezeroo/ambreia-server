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

public class CreateGuildMessageHandler(IGrainFactory grainFactory, IWordFilter wordFilter)
    : IMessageHandler<CreateGuildMessage>
{
    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly IWordFilter _wordFilter = wordFilter;

    public async ValueTask HandleAsync(
        CreateGuildMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0)
        {
            return;
        }

        // Before the grain, which debits the group's price: a refused group costs nothing. Reason 1
        // is the client's own "Invalid Group name" (group.edit.fail.1), shown whatever dialog is open.
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
            await ctx.SendComposerAsync(new GuildEditFailedMessageComposer { Reason = 1 }, ct)
                .ConfigureAwait(false);

            return;
        }

        int? groupId = await _grainFactory
            .GetGroupDirectoryGrain()
            .CreateGroupAsync(
                ctx.PlayerId,
                name,
                description,
                message.PrimaryColorId,
                message.SecondaryColorId,
                message.BaseRoomId,
                message.BadgeParts,
                ct
            )
            .ConfigureAwait(false);

        // Validation failed (room not owned / already a guild base) — nothing to confirm.
        if (groupId is null)
        {
            return;
        }

        await ctx.SendComposerAsync(
                new GuildCreatedMessageComposer
                {
                    BaseRoomId = message.BaseRoomId,
                    GroupId = groupId.Value,
                },
                ct
            )
            .ConfigureAwait(false);
    }
}
