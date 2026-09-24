using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Orleans;
using Vortex.PacketHandlers.Vault.Events;
using Vortex.Primitives.Events;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Vault.Enums;
using Vortex.Protocol.Messages.Outgoing.Vault;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.PacketHandlers.Tests;

public sealed class VaultIncomeDepositedNotifierTests
{
    [Fact]
    public async Task ADeposit_PingsThePlayerWithItsCategory()
    {
        List<IComposer> sent = [];
        IPlayerPresenceGrain presence = FakeProxy.Create<IPlayerPresenceGrain>(call =>
        {
            if (
                call.Method.Name == nameof(IPlayerPresenceGrain.SendComposerAsync)
                && call.Args?[0] is IComposer composer
            )
            {
                sent.Add(composer);
            }

            return Task.CompletedTask;
        });
        IGrainFactory factory = FakeProxy.Create<IGrainFactory>(call =>
            call.Method.IsGenericMethod ? presence : null
        );

        await new VaultIncomeDepositedNotifier(factory).HandleAsync(
            new VaultIncomeDepositedEvent(new PlayerId(7), VaultRewardCategoryType.HabboClub),
            null!,
            default
        );

        sent.Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<IncomeRewardNotificationMessageComposer>()
            .Which.RewardCategory.Should()
            .Be(VaultRewardCategoryType.HabboClub);
    }
}
