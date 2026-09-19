using FluentAssertions;
using Vortex.Primitives.Badges;
using Xunit;

namespace Vortex.Players.Tests.Badges;

/// <summary>
///     The tier ladder. Every badge the hotel draws carries the answer of this one function, and a
///     wrong answer is invisible: the client renders whatever tier arrives without complaint.
/// </summary>
public sealed class BadgeRarityTests
{
    private static readonly BadgeRarityThresholds Defaults = new()
    {
        MinimumPlayers = 100,
        UniqueMaxOwners = 1,
        LegendaryMaxShare = 0.001,
        MythicalMaxShare = 0.005,
        EpicMaxShare = 0.02,
        RareMaxShare = 0.08,
        UncommonMaxShare = 0.25,
    };

    [Theory]
    // 10 000 players: 1 owner is unique, 10 is 0.1% (legendary), 50 is 0.5% (mythical),
    // 200 is 2% (epic), 800 is 8% (rare), 2 500 is 25% (uncommon), above that common.
    [InlineData(1, 10_000, BadgeRarityTier.Unique)]
    [InlineData(10, 10_000, BadgeRarityTier.Legendary)]
    [InlineData(50, 10_000, BadgeRarityTier.Mythical)]
    [InlineData(200, 10_000, BadgeRarityTier.Epic)]
    [InlineData(800, 10_000, BadgeRarityTier.Rare)]
    [InlineData(2_500, 10_000, BadgeRarityTier.Uncommon)]
    [InlineData(9_000, 10_000, BadgeRarityTier.Common)]
    public void Calculate_PicksTheRarestTierTheBadgeFitsIn(
        int owners,
        int players,
        BadgeRarityTier expected
    ) => BadgeRarity.Calculate(owners, players, Defaults).Should().Be(expected);

    [Fact]
    public void ACodeNobodyHoldsIsCommon_NotUnique()
    {
        // Zero owners means the code has never been granted. Calling that the rarest thing in the
        // hotel would rank every unused code above the ones players actually earned.
        BadgeRarity.Calculate(0, 10_000, Defaults).Should().Be(BadgeRarityTier.Common);
    }

    [Fact]
    public void BelowTheMinimumPlayerCount_EverythingIsCommon()
    {
        // A hotel with eleven accounts would otherwise call its welcome badge legendary on day one.
        BadgeRarity.Calculate(1, 11, Defaults).Should().Be(BadgeRarityTier.Common);
    }

    [Fact]
    public void UniqueIsAnAbsoluteCount_NotAShare()
    {
        // One owner out of a hundred is 1%, which the share ladder would call epic. Unique wins
        // because a badge one player holds is unique at any hotel size.
        BadgeRarity.Calculate(1, 100, Defaults).Should().Be(BadgeRarityTier.Unique);
    }
}
