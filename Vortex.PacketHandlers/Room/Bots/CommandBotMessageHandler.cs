using System;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.PacketHandlers.Moderation;
using Vortex.Primitives.Bots;
using Vortex.Primitives.Moderation.WordFilter;
using Vortex.Primitives.Orleans;
using Vortex.Protocol.Messages.Incoming.Room.Bots;

namespace Vortex.PacketHandlers.Room.Bots;

public class CommandBotMessageHandler(IGrainFactory grainFactory, IWordFilter wordFilter)
    : IMessageHandler<CommandBotMessage>
{
    public async ValueTask HandleAsync(
        CommandBotMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0 || ctx.RoomId <= 0 || message.BotId <= 0)
        {
            return;
        }

        string? data = message.CommandId switch
        {
            // The whole name, as a key: the wired bot actions find a bot by it, so a name with
            // "bobba" spliced in would quietly detach the bot from every wired aimed at it.
            BotSkillId.ChangeName => await wordFilter
                .PassAsync(ctx, message.Data, WordFilterSource.BotName, ct)
                .ConfigureAwait(false),
            BotSkillId.Chatter => await FilterChatterAsync(ctx, message.Data, ct)
                .ConfigureAwait(false),
            // The other skills carry no text.
            _ => message.Data,
        };

        if (data is null)
        {
            return;
        }

        await grainFactory
            .GetRoomBots(ctx.RoomId)
            .SetBotSkillAsync(ctx.AsActionContext(), message.BotId, message.CommandId, data, ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// A chatter blob is <c>lines;#;autoChat;#;delay;#;markov</c> — or the legacy form with plain
    /// <c>;</c> between fields, see <see cref="BotChatterConfiguration.Parse"/>. Only the lines are
    /// prose; the settings after them come back byte for byte.
    /// </summary>
    private async ValueTask<string?> FilterChatterAsync(
        MessageContext ctx,
        string data,
        CancellationToken ct
    )
    {
        int split = data.IndexOf(BotChatterConfiguration.FieldSeparator, StringComparison.Ordinal);

        if (split < 0)
        {
            split = data.IndexOf(';', StringComparison.Ordinal);
        }

        string lines = split < 0 ? data : data[..split];
        string settings = split < 0 ? string.Empty : data[split..];

        string? filtered = await wordFilter
            .PassAsync(ctx, lines, WordFilterSource.BotChatter, ct)
            .ConfigureAwait(false);

        return filtered is null ? null : filtered + settings;
    }
}
