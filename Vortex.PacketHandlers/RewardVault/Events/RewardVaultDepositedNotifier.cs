using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Orleans;
using Vortex.Protocol.Messages.Outgoing.RewardVault;

namespace Vortex.PacketHandlers.RewardVault.Events;

/// <summary>
/// Tells an online player that something new is waiting in their Earnings window. The client lights
/// the purse indicator and posts <c>notification.earning.new</c> (EarningsController.as:139); an
/// offline player simply sees it on their next window open.
/// </summary>
/// <remarks>
/// Here rather than in the vault grain, which is kept protocol-free.
/// </remarks>
public sealed class RewardVaultDepositedNotifier(IGrainFactory grainFactory)
    : IEventHandler<RewardVaultDepositedEvent>
{
    public async ValueTask HandleAsync(
        RewardVaultDepositedEvent e,
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
