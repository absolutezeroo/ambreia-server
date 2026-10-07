using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Orleans;
using Vortex.Players.RewardVault;
using Vortex.Primitives.Commerce;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Players.Wallet;
using Vortex.Primitives.RewardVault.Enums;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Players.Tests.RewardVault;

public sealed class RewardVaultSettlerTests
{
    private const int PlayerId = 7;

    [Fact]
    public async Task ACurrencyRow_IsCreditedUnderAnIdDerivedFromTheRow()
    {
        List<ProxyCall> credits = [];
        CurrencyRewardVaultSettler settler = new(WalletFactory(credits, answer: true));

        await settler.SettleAsync(
            PlayerId,
            Row(41, RewardVaultType.Duckets, 15),
            CancellationToken.None
        );

        credits.Should().ContainSingle();
        ((List<WalletDebitRequest>)credits[0].Args![0]!)
            .Should()
            .ContainSingle()
            .Which.Amount.Should()
            .Be(15);
        ((CommerceOperationId)credits[0].Args![1]!)
            .Should()
            .Be(CommerceOperationId.Deterministic(CommerceOperationKind.RewardVault, 41));
        credits[0].Args![2].Should().Be(CommerceStepKeys.VAULT_CLAIM);
    }

    [Fact]
    public async Task AlreadyCredited_StillCountsAsSettled()
    {
        // CreditOnceAsync answers false for "this receipt already exists": the row was paid by an
        // earlier claim that crashed before marking it, and marking it now is the right outcome.
        CurrencyRewardVaultSettler settler = new(WalletFactory([], answer: false));

        await settler
            .Invoking(s =>
                s.SettleAsync(PlayerId, Row(41, RewardVaultType.Credits, 5), CancellationToken.None)
            )
            .Should()
            .NotThrowAsync();
    }

    [Fact]
    public void TwoSettlersForOneType_IsAWiringError()
    {
        IGrainFactory factory = WalletFactory([], answer: true);

        FluentActions
            .Invoking(() =>
                new RewardVaultSettlerMap([
                    new CurrencyRewardVaultSettler(factory),
                    new CurrencyRewardVaultSettler(factory),
                ])
            )
            .Should()
            .Throw<System.InvalidOperationException>();
    }

    [Fact]
    public void AProductRow_HasNoSettlerYet()
    {
        RewardVaultSettlerMap map = new([new CurrencyRewardVaultSettler(WalletFactory([], true))]);

        map.TryGet(RewardVaultType.Product, out _).Should().BeFalse();
        map.TryGet(RewardVaultType.Credits, out _).Should().BeTrue();
    }

    private static RewardVaultRow Row(int id, RewardVaultType type, int amount) =>
        new(id, RewardVaultCategoryType.Achievements, type, amount, string.Empty);

    private static IGrainFactory WalletFactory(List<ProxyCall> credits, bool answer)
    {
        IPlayerWalletGrain wallet = FakeProxy.Create<IPlayerWalletGrain>(call =>
        {
            if (call.Method.Name == nameof(IPlayerWalletGrain.CreditOnceAsync))
            {
                credits.Add(call);
                return Task.FromResult(answer);
            }

            return null;
        });

        return FakeProxy.Create<IGrainFactory>(call =>
            call.Method.IsGenericMethod
            && call.Method.GetGenericArguments()[0] == typeof(IPlayerWalletGrain)
                ? wallet
                : null
        );
    }
}
