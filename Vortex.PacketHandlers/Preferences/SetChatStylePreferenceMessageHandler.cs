using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players.Grains;
using Vortex.Protocol.Messages.Incoming.Preferences;

namespace Vortex.PacketHandlers.Preferences;

public class SetChatStylePreferenceMessageHandler(IGrainFactory grainFactory)
    : IMessageHandler<SetChatStylePreferenceMessage>
{
    private readonly IGrainFactory _grainFactory = grainFactory;

    public async ValueTask HandleAsync(
        SetChatStylePreferenceMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0)
        {
            return;
        }

        // Orchestration-only: the grain owns the persistence. Only the font size is kept — the
        // client reads it back off the account-preferences packet
        // (HabboFreeFlowChat::onAccountPreferences), so one we do not store is one the player
        // re-picks after every login. The style half went with chat styles (M0 bis): the parser
        // reads it and drops it.
        await _grainFactory
            .GetPlayerGrain(ctx.PlayerId)
            .SetChatSizePreferenceAsync(message.FontSizeMode, ct)
            .ConfigureAwait(false);
    }
}
