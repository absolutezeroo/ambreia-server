using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Orleans.Snapshots.RewardVault;
using Vortex.Protocol.Messages.Incoming.RewardVault;
using Vortex.Protocol.Messages.Outgoing.RewardVault;

namespace Vortex.PacketHandlers.RewardVault;

public class IncomeRewardStatusMessageHandler(IGrainFactory grainFactory)
    : IMessageHandler<IncomeRewardStatusMessage>
{
    private readonly IGrainFactory _grainFactory = grainFactory;

    public async ValueTask HandleAsync(
        IncomeRewardStatusMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0)
        {
            return;
        }

        List<IncomeRewardSnapshot> rewards = await _grainFactory
            .GetPlayerRewardVaultGrain(ctx.PlayerId)
            .GetIncomeRewardsAsync(ct)
            .ConfigureAwait(false);

        await ctx.SendComposerAsync(
                new IncomeRewardStatusMessageComposer { IncomeRewards = rewards },
                ct
            )
            .ConfigureAwait(false);
    }
}
