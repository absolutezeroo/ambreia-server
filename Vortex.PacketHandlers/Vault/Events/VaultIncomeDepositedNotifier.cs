using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Orleans;
using Vortex.Protocol.Messages.Outgoing.Vault;

namespace Vortex.PacketHandlers.Vault.Events;

/// <summary>
/// Tells an online player that something new is waiting in their Earnings window. The client lights
/// the purse indicator and posts <c>notification.earning.new</c> (EarningsController.as:139); an
/// offline player simply sees it on their next window open.
/// </summary>
/// <remarks>
/// Here rather than in the vault grain because <c>Vortex.Collectibles</c> is protocol-free.
/// </remarks>
public sealed class VaultIncomeDepositedNotifier(IGrainFactory grainFactory)
    : IEventHandler<VaultIncomeDepositedEvent>
{
    public async ValueTask HandleAsync(
        VaultIncomeDepositedEvent e,
        EventContext ctx,
        CancellationToken ct
    ) =>
        await grainFactory
            .GetPlayerPresenceGrain(e.PlayerId)
            .SendComposerAsync(
                new IncomeRewardNotificationMessageComposer { RewardCategory = e.Category }
            )
            .ConfigureAwait(false);
}
