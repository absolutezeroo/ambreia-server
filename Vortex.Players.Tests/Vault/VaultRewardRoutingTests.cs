using System;
using FluentAssertions;
using Vortex.Primitives.Players.Enums.Wallet;
using Vortex.Primitives.Players.Wallet;
using Vortex.Primitives.Vault;
using Vortex.Primitives.Vault.Enums;
using Xunit;

namespace Vortex.Players.Tests.Vault;

public sealed class VaultRewardRoutingTests
{
    private static readonly CurrencyKind Credits = new() { CurrencyType = CurrencyType.Credits };
    private static readonly CurrencyKind Duckets = new()
    {
        CurrencyType = CurrencyType.ActivityPoints,
        ActivityPointType = 0,
    };
    private static readonly CurrencyKind Diamonds = new()
    {
        CurrencyType = CurrencyType.ActivityPoints,
        ActivityPointType = 5,
    };

    [Fact]
    public void Credits_AreRewardTypeOne()
    {
        VaultRewardRouting.TryGetRewardType(Credits, out VaultRewardType type).Should().BeTrue();
        type.Should().Be(VaultRewardType.Credits);
        ((int)type).Should().Be(1);
    }

    [Fact]
    public void Duckets_AreRewardTypeZero()
    {
        VaultRewardRouting.TryGetRewardType(Duckets, out VaultRewardType type).Should().BeTrue();
        type.Should().Be(VaultRewardType.Duckets);
    }

    [Fact]
    public void AnyOtherCurrency_HasNoVaultType()
    {
        VaultRewardRouting.TryGetRewardType(Diamonds, out _).Should().BeFalse();
        VaultRewardRouting
            .TryGetRewardType(new CurrencyKind { CurrencyType = CurrencyType.Emeralds }, out _)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void RoundTrips_BackToTheWalletCurrency()
    {
        VaultRewardRouting.ToCurrencyKind(VaultRewardType.Credits).Should().Be(Credits);
        VaultRewardRouting.ToCurrencyKind(VaultRewardType.Duckets).Should().Be(Duckets);
    }

    [Fact]
    public void Product_IsNotACurrency()
    {
        Action act = () => VaultRewardRouting.ToCurrencyKind(VaultRewardType.Product);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
