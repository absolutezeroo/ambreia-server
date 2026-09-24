using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Orleans;
using Vortex.Protocol.Messages.Incoming.Vault;
using Vortex.Protocol.Messages.Outgoing.Vault;

namespace Vortex.PacketHandlers.Vault;

public class IncomeRewardClaimMessageHandler(IGrainFactory grainFactory)
    : IMessageHandler<IncomeRewardClaimMessage>
{
    private readonly IGrainFactory _grainFactory = grainFactory;

    public async ValueTask HandleAsync(
        IncomeRewardClaimMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0)
        {
            return;
        }

        bool success = await _grainFactory
            .GetPlayerVaultGrain(ctx.PlayerId)
            .ClaimAsync(message.Category, ct)
            .ConfigureAwait(false);

        await ctx.SendComposerAsync(
                new IncomeRewardClaimResponseMessageComposer
                {
                    RewardCategory = message.Category,
                    Result = success,
                },
                ct
            )
            .ConfigureAwait(false);
    }
}
