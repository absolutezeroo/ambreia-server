using System;
using System.Collections.Generic;
using FluentAssertions;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Loot;
using Vortex.Primitives.Loot.Snapshots;
using Xunit;

namespace Vortex.Players.Tests.Loot;

/// <summary>
/// Locks the draw the server does on the player's behalf. The client never sends an outcome, so
/// these rules are the only thing standing between a prize pool and someone winning a prize that was
/// meant for another variant, or a disabled/zero-weight row leaking back into circulation.
/// </summary>
public sealed class LootPickerTests
{
    private const string BoxPool = "box";
    private const string TrophyPool = "trophy";

    private static LootEntrySnapshot Entry(
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
        LootPicker.Pick([], BoxPool, "purple", _ => 0).Should().BeNull();
    }

    [Fact]
    public void EntriesFromAnotherPool_AreNeverDrawn()
    {
        List<LootEntrySnapshot> entries = [Entry(1, TrophyPool)];

        LootPicker.Pick(entries, BoxPool, "purple", _ => 0).Should().BeNull();
    }

    [Fact]
    public void VariantRestrictedEntries_OnlyDropFromTheirOwnVariant()
    {
        List<LootEntrySnapshot> entries = [Entry(1, variant: "blue")];

        LootPicker.Pick(entries, BoxPool, "purple", _ => 0).Should().BeNull();
        LootPicker.Pick(entries, BoxPool, "blue", _ => 0)!.Id.Should().Be(1);
    }

    [Fact]
    public void VariantlessEntries_DropFromEveryVariant()
    {
        List<LootEntrySnapshot> entries = [Entry(1)];

        LootPicker.Pick(entries, BoxPool, "turquoise", _ => 0)!.Id.Should().Be(1);
    }

    [Fact]
    public void VariantMatching_IgnoresCasingAndPadding()
    {
        List<LootEntrySnapshot> entries = [Entry(1, variant: "red")];

        LootPicker.Pick(entries, BoxPool, "  RED  ", _ => 0)!.Id.Should().Be(1);
    }

    [Fact]
    public void PoolMatching_IsCaseSensitive_SoATypoedCodeDrawsNothing()
    {
        List<LootEntrySnapshot> entries = [Entry(1)];

        // Codes are normalized to lowercase on write, so a mismatch here is a caller bug, not a
        // formatting difference to be forgiven — drawing from the wrong pool would be worse.
        LootPicker.Pick(entries, "Mystery-Box", "purple", _ => 0).Should().BeNull();
    }

    [Fact]
    public void NonPositiveWeights_AreExcludedFromTheDraw()
    {
        List<LootEntrySnapshot> entries = [Entry(1, weight: 0), Entry(2, weight: -5)];

        LootPicker.Pick(entries, BoxPool, "purple", _ => 0).Should().BeNull();
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
        List<LootEntrySnapshot> entries =
        [
            Entry(1, weight: 1),
            Entry(2, weight: 3),
            Entry(3, weight: 6),
        ];

        int seenTotal = -1;

        LootEntrySnapshot? picked = LootPicker.Pick(
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
        List<LootEntrySnapshot> entries =
        [
            Entry(1, weight: 2),
            Entry(2, variant: "blue", weight: 50),
            Entry(3, poolCode: TrophyPool, weight: 50),
        ];

        int seenTotal = -1;

        LootPicker.Pick(
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
        List<LootEntrySnapshot> entries = [Entry(1, weight: 3)];

        Action pick = () => LootPicker.Pick(entries, BoxPool, "purple", _ => 3);

        pick.Should().Throw<ArgumentOutOfRangeException>();
    }
}
