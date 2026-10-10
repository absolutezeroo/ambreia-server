using System.Linq;
using FluentAssertions;
using Vortex.Rpg.Combat.Telegraphs;
using Vortex.Rpg.Content.Definitions;
using Xunit;

namespace Vortex.Rpg.Combat.Tests;

/// <summary>The cells of each telegraph shape (spec M1c §8.1).</summary>
public sealed class TelegraphCellsTests
{
    private static bool Open(int x, int y) => true;

    private static SkillTelegraph Circle(int size) =>
        new() { Shape = TelegraphShape.Circle, Size = size };

    private static SkillTelegraph Line(int size, int width = 1) =>
        new()
        {
            Shape = TelegraphShape.Line,
            Size = size,
            Width = width,
        };

    private static SkillTelegraph Cone(int size, int angle) =>
        new()
        {
            Shape = TelegraphShape.Cone,
            Size = size,
            Angle = angle,
        };

    [Fact]
    public void ACircleOfOne_IsACross() =>
        TelegraphCells
            .Of(Circle(1), (5, 5), (5, 5), Open)
            .Should()
            .BeEquivalentTo([(5, 5), (4, 5), (6, 5), (5, 4), (5, 6)]);

    [Fact]
    public void ACircleOfTwo_HasThirteenCells() =>
        TelegraphCells.Of(Circle(2), (5, 5), (5, 5), Open).Should().HaveCount(13);

    [Fact]
    public void TheCells_AreNearestFirst() =>
        TelegraphCells.Of(Circle(2), (5, 5), (5, 5), Open)[0].Should().Be((5, 5));

    [Fact]
    public void AnEastLine_IsThreeCellsAhead() =>
        TelegraphCells.Of(Line(3), (0, 0), (5, 0), Open).Should().Equal((1, 0), (2, 0), (3, 0));

    [Fact]
    public void ADiagonalLine_FollowsTheDiagonal() =>
        TelegraphCells.Of(Line(3), (0, 0), (5, 5), Open).Should().Equal((1, 1), (2, 2), (3, 3));

    [Fact]
    public void AnEastLine_OfWidthThree_IsThreeRowsOfThree() =>
        TelegraphCells
            .Of(Line(3, width: 3), (0, 0), (5, 0), Open)
            .Should()
            .BeEquivalentTo([
                (1, -1),
                (1, 0),
                (1, 1),
                (2, -1),
                (2, 0),
                (2, 1),
                (3, -1),
                (3, 0),
                (3, 1),
            ]);

    [Fact]
    public void ALineOfWidthFive_IsFiveWide() =>
        TelegraphCells.Of(Line(2, width: 5), (0, 0), (0, 9), Open).Should().HaveCount(10);

    [Fact]
    public void AQuarterCone_OpensFortyFiveDegreesEachSide() =>
        TelegraphCells
            .Of(Cone(2, 90), (0, 0), (5, 0), Open)
            .Should()
            .BeEquivalentTo([(1, 0), (1, 1), (1, -1), (2, 0)]);

    [Fact]
    public void AHalfCone_IsAHalfDisc() =>
        TelegraphCells
            .Of(Cone(1, 180), (0, 0), (5, 0), Open)
            .Should()
            .BeEquivalentTo([(1, 0), (0, 1), (0, -1)]);

    [Fact]
    public void ACone_NeverHoldsItsCaster() =>
        TelegraphCells.Of(Cone(3, 180), (0, 0), (5, 0), Open).Should().NotContain((0, 0));

    [Fact]
    public void ACone_IsTheSameTurnedByAQuarter()
    {
        int[] straight =
        [
            .. new[] { (9, 0), (0, 9), (-9, 0), (0, -9) }.Select(t =>
                TelegraphCells.Of(Cone(3, 90), (0, 0), t, Open).Count
            ),
        ];
        int[] diagonal =
        [
            .. new[] { (9, 9), (-9, 9), (-9, -9), (9, -9) }.Select(t =>
                TelegraphCells.Of(Cone(3, 90), (0, 0), t, Open).Count
            ),
        ];

        straight.Distinct().Should().ContainSingle();
        diagonal.Distinct().Should().ContainSingle();
    }

    [Fact]
    public void ACellThatCannotBeWalked_IsLeftOut() =>
        TelegraphCells
            .Of(Line(3), (0, 0), (5, 0), (x, y) => (x, y) != (2, 0))
            .Should()
            .Equal((1, 0), (3, 0));

    [Fact]
    public void NoAim_PointsEast() =>
        TelegraphCells.Of(Line(2), (4, 4), (4, 4), Open).Should().Equal((5, 4), (6, 4));
}
