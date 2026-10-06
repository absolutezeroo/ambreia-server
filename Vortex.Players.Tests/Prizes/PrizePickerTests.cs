using System;
using System.Collections.Generic;
using FluentAssertions;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Prizes;
using Vortex.Primitives.Prizes.Snapshots;
using Xunit;

namespace Vortex.Players.Tests.Prizes;

/// <summary>
/// Locks the draw the server does on the player's behalf. The client never sends an outcome, so
/// these rules are the only thing standing between a prize pool and someone winning a prize that was
/// meant for another variant, or a disabled/zero-weight row leaking back into circulation.
/// </summary>
public sealed class PrizePickerTests
{
    private const string BoxPool = "box";
    private const string TrophyPool = "trophy";

    private static PrizeEntrySnapshot Entry(
        int id,
        string poolCode = BoxPool,
        string variant = "",
        int weight = 1
    ) =>
        new()
        {
            Id = id,
            PoolCode = poolCode,
            Variant = variant,
            ProductType = ProductType.Floor,
            FurnitureDefinitionId = id,
            ExtraParam = string.Empty,
            Weight = weight,
        };

    [Fact]
    public void EmptyPool_YieldsNothing()
    {
        PrizePicker.Pick([], BoxPool, "purple", _ => 0).Should().BeNull();
    }

    [Fact]
    public void EntriesFromAnotherPool_AreNeverDrawn()
    {
        List<PrizeEntrySnapshot> entries = [Entry(1, TrophyPool)];

        PrizePicker.Pick(entries, BoxPool, "purple", _ => 0).Should().BeNull();
    }

    [Fact]
    public void VariantRestrictedEntries_OnlyDropFromTheirOwnVariant()
    {
        List<PrizeEntrySnapshot> entries = [Entry(1, variant: "blue")];

        PrizePicker.Pick(entries, BoxPool, "purple", _ => 0).Should().BeNull();
        PrizePicker.Pick(entries, BoxPool, "blue", _ => 0)!.Id.Should().Be(1);
    }

    [Fact]
    public void VariantlessEntries_DropFromEveryVariant()
    {
        List<PrizeEntrySnapshot> entries = [Entry(1)];

        PrizePicker.Pick(entries, BoxPool, "turquoise", _ => 0)!.Id.Should().Be(1);
    }

    [Fact]
    public void VariantMatching_IgnoresCasingAndPadding()
    {
        List<PrizeEntrySnapshot> entries = [Entry(1, variant: "red")];

        PrizePicker.Pick(entries, BoxPool, "  RED  ", _ => 0)!.Id.Should().Be(1);
    }

    [Fact]
    public void PoolMatching_IsCaseSensitive_SoATypoedCodeDrawsNothing()
    {
        List<PrizeEntrySnapshot> entries = [Entry(1)];

        // Codes are normalized to lowercase on write, so a mismatch here is a caller bug, not a
        // formatting difference to be forgiven — drawing from the wrong pool would be worse.
        PrizePicker.Pick(entries, "Mystery-Box", "purple", _ => 0).Should().BeNull();
    }

    [Fact]
    public void NonPositiveWeights_AreExcludedFromTheDraw()
    {
        List<PrizeEntrySnapshot> entries = [Entry(1, weight: 0), Entry(2, weight: -5)];

        PrizePicker.Pick(entries, BoxPool, "purple", _ => 0).Should().BeNull();
    }

    [Theory]
    // Weights 1 / 3 / 6 carve [0,1) [1,4) [4,10); every boundary must land in the right bucket.
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(3, 2)]
    [InlineData(4, 3)]
    [InlineData(9, 3)]
    public void WeightsCarveContiguousRanges(int roll, int expectedId)
    {
        List<PrizeEntrySnapshot> entries =
        [
            Entry(1, weight: 1),
            Entry(2, weight: 3),
            Entry(3, weight: 6),
        ];

        int seenTotal = -1;

        PrizeEntrySnapshot? picked = PrizePicker.Pick(
            entries,
            BoxPool,
            "purple",
            total =>
            {
                seenTotal = total;
                return roll;
            }
        );

        seenTotal.Should().Be(10);
        picked!.Id.Should().Be(expectedId);
    }

    [Fact]
    public void TotalWeightExcludesIneligibleRows()
    {
        List<PrizeEntrySnapshot> entries =
        [
            Entry(1, weight: 2),
            Entry(2, variant: "blue", weight: 50),
            Entry(3, poolCode: TrophyPool, weight: 50),
        ];

        int seenTotal = -1;

        PrizePicker.Pick(
            entries,
            BoxPool,
            "purple",
            total =>
            {
                seenTotal = total;
                return 0;
            }
        );

        seenTotal.Should().Be(2);
    }

    [Fact]
    public void OutOfRangeRoll_Throws_RatherThanReadingAsAnEmptyPool()
    {
        List<PrizeEntrySnapshot> entries = [Entry(1, weight: 3)];

        Action pick = () => PrizePicker.Pick(entries, BoxPool, "purple", _ => 3);

        pick.Should().Throw<ArgumentOutOfRangeException>();
    }
}
