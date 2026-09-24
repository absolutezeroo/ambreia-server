using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Orleans;
using Vortex.Collectibles.Vault;
using Vortex.Primitives.Commerce;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Players.Wallet;
using Vortex.Primitives.Vault.Enums;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Players.Tests.Vault;

public sealed class VaultRewardSettlerTests
{
    private const int PlayerId = 7;

    [Fact]
    public async Task ACurrencyRow_IsCreditedUnderAnIdDerivedFromTheRow()
    {
        List<ProxyCall> credits = [];
        CurrencyVaultRewardSettler settler = new(WalletFactory(credits, answer: true));

        await settler.SettleAsync(
            PlayerId,
            Row(41, VaultRewardType.Duckets, 15),
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
            .Be(CommerceOperationId.Deterministic(CommerceOperationKind.VaultIncome, 41));
        credits[0].Args![2].Should().Be(CommerceStepKeys.VAULT_CLAIM);
    }

    [Fact]
    public async Task AlreadyCredited_StillCountsAsSettled()
    {
        // CreditOnceAsync answers false for "this receipt already exists": the row was paid by an
        // earlier claim that crashed before marking it, and marking it now is the right outcome.
        CurrencyVaultRewardSettler settler = new(WalletFactory([], answer: false));

        await settler
            .Invoking(s =>
                s.SettleAsync(PlayerId, Row(41, VaultRewardType.Credits, 5), CancellationToken.None)
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
                new VaultRewardSettlerMap([
                    new CurrencyVaultRewardSettler(factory),
                    new CurrencyVaultRewardSettler(factory),
                ])
            )
            .Should()
            .Throw<System.InvalidOperationException>();
    }

    [Fact]
    public void AProductRow_HasNoSettlerYet()
    {
        VaultRewardSettlerMap map = new([new CurrencyVaultRewardSettler(WalletFactory([], true))]);

        map.TryGet(VaultRewardType.Product, out _).Should().BeFalse();
        map.TryGet(VaultRewardType.Credits, out _).Should().BeTrue();
    }

    private static VaultIncomeRow Row(int id, VaultRewardType type, int amount) =>
        new(id, VaultRewardCategoryType.Achievements, type, amount, string.Empty);

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
