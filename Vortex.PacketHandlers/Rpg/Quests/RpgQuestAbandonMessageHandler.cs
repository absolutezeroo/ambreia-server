using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Orleans;
using Vortex.Protocol.Messages.Incoming.Rpg.Quests;
using Vortex.Protocol.Messages.Outgoing.Rpg.Quests;

namespace Vortex.PacketHandlers.Rpg.Quests;

/// <summary>The player drops a quest (spec M2a §3.1): his own log answers, and he is told.</summary>
public class RpgQuestAbandonMessageHandler(IGrainFactory grainFactory)
    : IMessageHandler<RpgQuestAbandonMessage>
{
    public async ValueTask HandleAsync(
        RpgQuestAbandonMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0)
        {
            return;
        }

        string code = await grainFactory
            .GetQuestLogGrain(ctx.PlayerId)
            .AbandonAsync(message.QuestId)
            .ConfigureAwait(false);

        await grainFactory
            .GetPlayerPresenceGrain(ctx.PlayerId)
            .SendComposerAsync(
                new RpgQuestResultMessageComposer
                {
                    Action = "abandoned",
                    Code = code,
                    QuestId = message.QuestId,
                }
            )
            .ConfigureAwait(false);
    }
}
