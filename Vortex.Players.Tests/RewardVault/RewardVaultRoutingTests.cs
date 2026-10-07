using System;
using FluentAssertions;
using Vortex.Primitives.Players.Enums.Wallet;
using Vortex.Primitives.Players.Wallet;
using Vortex.Primitives.RewardVault;
using Vortex.Primitives.RewardVault.Enums;
using Xunit;

namespace Vortex.Players.Tests.RewardVault;

public sealed class RewardVaultRoutingTests
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
        RewardVaultRouting.TryGetRewardType(Credits, out RewardVaultType type).Should().BeTrue();
        type.Should().Be(RewardVaultType.Credits);
        ((int)type).Should().Be(1);
    }

    [Fact]
    public void Duckets_AreRewardTypeZero()
    {
        RewardVaultRouting.TryGetRewardType(Duckets, out RewardVaultType type).Should().BeTrue();
        type.Should().Be(RewardVaultType.Duckets);
    }

    [Fact]
    public void AnyOtherCurrency_HasNoRewardVaultType()
    {
        RewardVaultRouting.TryGetRewardType(Diamonds, out _).Should().BeFalse();
        RewardVaultRouting
            .TryGetRewardType(new CurrencyKind { CurrencyType = CurrencyType.Emeralds }, out _)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void RoundTrips_BackToTheWalletCurrency()
    {
        RewardVaultRouting.ToCurrencyKind(RewardVaultType.Credits).Should().Be(Credits);
        RewardVaultRouting.ToCurrencyKind(RewardVaultType.Duckets).Should().Be(Duckets);
    }

    [Fact]
    public void Product_IsNotACurrency()
    {
        Action act = () => RewardVaultRouting.ToCurrencyKind(RewardVaultType.Product);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
