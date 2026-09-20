using System.Collections.Immutable;
using FluentAssertions;
using Vortex.Primitives.RewardTracks;
using Vortex.Primitives.RewardTracks.Snapshots;
using Vortex.RewardTracks.Progression;
using Xunit;

namespace Vortex.Rewards.Tests;

/// <summary>
/// What each filter operator actually decides.
/// </summary>
/// <remarks>
/// The ordering and negating operators are here because their failure mode is the quiet one: a step
/// that never matches does not error, it just stops advancing, and a step that always matches pays
/// out for behaviour nobody asked for. The comparison is on strings, so both directions have to be
/// pinned.
/// </remarks>
public sealed class StepFilterOperatorTests
{
    /// <summary>Whether one signal carrying one fact satisfies a single-step task.</summary>
    private static bool Matches(
        string factKey,
        StepFilterOperator op,
        string filterValue,
        string signalValue
    )
    {
        RewardTrackTaskDefinitionSnapshot task = Content.Sequence(
            steps:
            [
                Content.Step(
                    RewardTrackActions.BuyFromCatalogue,
                    Content.Filter(factKey, op, filterValue)
                ),
            ]
        );

        TaskProgressOutcome outcome = TaskProgressRules.Apply(
            task,
            0,
            -1,
            string.Empty,
            0,
            string.Empty,
            RewardTrackActions.BuyFromCatalogue,
            1,
            null,
            [new RewardTrackFactSnapshot(factKey, signalValue)],
            premiumUnlocked: false
        );

        return outcome.NewProgress > 0;
    }

    [Theory]
    [InlineData("100", "99", false)]
    [InlineData("100", "100", true)]
    [InlineData("100", "101", true)]
    // Multi-digit against single-digit: the whole point of not comparing these as strings, where
    // "9" sorts above "100" and "spend at least 100" would pay for spending nine.
    [InlineData("100", "9", false)]
    public void At_least_compares_as_a_number(string filter, string signal, bool expected) =>
        Matches(RewardTrackFacts.Price, StepFilterOperator.AtLeast, filter, signal)
            .Should()
            .Be(expected);

    [Theory]
    [InlineData("100", "101", false)]
    [InlineData("100", "100", true)]
    [InlineData("100", "9", true)]
    public void At_most_compares_as_a_number(string filter, string signal, bool expected) =>
        Matches(RewardTrackFacts.Price, StepFilterOperator.AtMost, filter, signal)
            .Should()
            .Be(expected);

    [Fact]
    public void A_numeric_operator_over_something_that_is_not_a_number_never_matches()
    {
        // Not an exception, and not a pass. An operator who moved a filter from price to a room
        // name and left "at least" behind would otherwise have a step that matches every room in
        // the hotel.
        Matches(RewardTrackFacts.RoomName, StepFilterOperator.AtLeast, "0", "Casino Royale")
            .Should()
            .BeFalse();
    }

    [Theory]
    // Barefoot: the shoe part is simply absent, so there is no value to match on.
    [InlineData("hd-180-1.ch-210-66.lg-270-82", true)]
    [InlineData("hd-180-1.ch-210-66.lg-270-82.sh-290-91", false)]
    public void Not_contains_is_how_an_absent_part_is_written(string figure, bool expected) =>
        Matches(RewardTrackFacts.Figure, StepFilterOperator.NotContains, "sh-", figure)
            .Should()
            .Be(expected);

    [Theory]
    [InlineData("4", true)]
    [InlineData("2", false)]
    public void Not_one_of_excludes_the_whole_list(string signal, bool expected) =>
        Matches(RewardTrackFacts.Room, StepFilterOperator.NotOneOf, "1,2,3", signal)
            .Should()
            .Be(expected);

    // What a look actually carries: several garments at once, the shoes among them.
    private const string Worn = "1,115,290,3222";

    [Theory]
    // The picker's ordinary output: one garment.
    [InlineData("290", true)]
    [InlineData("295", false)]
    // Several, which is "wearing any of this season's shoes".
    [InlineData("295,290", true)]
    [InlineData("291,292", false)]
    // The reason this operator exists rather than Contains: 1 is worn, and 11 is a prefix match on
    // 115 that no set id in this look is.
    [InlineData("1", true)]
    [InlineData("11", false)]
    [InlineData("32", false)]
    public void Has_any_of_asks_the_signal_what_it_is_wearing(string filter, bool expected) =>
        Matches(RewardTrackFacts.FigureSet, StepFilterOperator.HasAnyOf, filter, Worn)
            .Should()
            .Be(expected);

    [Theory]
    [InlineData("290", false)]
    [InlineData("295", true)]
    public void Has_none_of_is_its_negation(string filter, bool expected) =>
        Matches(RewardTrackFacts.FigureSet, StepFilterOperator.HasNoneOf, filter, Worn)
            .Should()
            .Be(expected);

    [Fact]
    public void One_of_would_have_answered_the_wrong_question()
    {
        // Kept as a test rather than a comment: OneOf asks whether the fact's whole value appears in
        // the filter's list, and the fact here is a list of its own. It matches nothing, which is
        // exactly the silent never-advancing task this operator pair exists to prevent.
        Matches(RewardTrackFacts.FigureSet, StepFilterOperator.OneOf, "290", Worn)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void A_negating_operator_still_needs_the_fact_to_be_there()
    {
        // The same rule NotEquals already follows: "wearing no shoes" is a claim about a figure,
        // and a signal carrying none has not made it true. Otherwise every unrelated action in the
        // hotel would satisfy the step.
        RewardTrackTaskDefinitionSnapshot task = Content.Sequence(
            steps:
            [
                Content.Step(
                    RewardTrackActions.BuyFromCatalogue,
                    Content.Filter(RewardTrackFacts.Figure, StepFilterOperator.NotContains, "sh-")
                ),
            ]
        );

        TaskProgressRules
            .Apply(
                task,
                0,
                -1,
                string.Empty,
                0,
                string.Empty,
                RewardTrackActions.BuyFromCatalogue,
                1,
                null,
                ImmutableArray<RewardTrackFactSnapshot>.Empty,
                premiumUnlocked: false
            )
            .NewProgress.Should()
            .Be(0);
    }
}
