using FluentAssertions;
using Vortex.Dashboard.API.Admin.Rules;
using Vortex.Primitives.RewardTracks;
using Vortex.Primitives.RewardTracks.Admin;
using Xunit;

namespace Vortex.Dashboard.Tests;

/// <summary>
/// What the sequence editor refuses before it is ever saved.
/// </summary>
/// <remarks>
/// These live with the dashboard because authoring is the dashboard's: the emulator walks sequences,
/// it does not judge them. The rules are checked against the REAL vocabulary the loaded translators
/// declare rather than a hand-written one, so a filter the editor accepts is one some action can
/// actually raise.
/// </remarks>
public sealed class RewardTrackSequenceRulesTests
{
    [Theory]
    // A fact the step's own action never emits: nothing would ever satisfy it.
    [InlineData(RewardTrackActions.PlaceItem, RewardTrackFacts.Player, "42")]
    // A reference to a step that has not run when this one is tested.
    [InlineData(RewardTrackActions.PlaceItem, RewardTrackFacts.Item, "$0")]
    [InlineData(RewardTrackActions.PlaceItem, RewardTrackFacts.Item, "$5")]
    // A list with one entry is Equals wearing a hat, and usually a wrong separator.
    [InlineData(RewardTrackActions.PlaceItem, RewardTrackFacts.Item, "500")]
    public void The_editor_refuses_a_filter_that_cannot_work(
        string action,
        string factKey,
        string value
    )
    {
        StepFilterOperator op =
            value == "500" ? StepFilterOperator.OneOf : StepFilterOperator.Equals;

        RewardTrackSequenceRules
            .FirstProblem(
                [new RewardTrackTaskStepSpec(action, [new(factKey, op, value)])],
                SignalVocabularyFixture.Real
            )
            .Should()
            .NotBeNull();
    }

    [Fact]
    public void The_editor_refuses_a_reference_to_a_step_that_never_recorded_that_fact()
    {
        // "The same furniture" only works between two steps that both talk about furniture. Chat
        // emits no item, so $0 could never resolve.
        RewardTrackSequenceRules
            .FirstProblem(
                [
                    new RewardTrackTaskStepSpec(RewardTrackActions.ChatWithSomeone, []),
                    new RewardTrackTaskStepSpec(
                        RewardTrackActions.WalkOnFurni,
                        [new(RewardTrackFacts.Item, StepFilterOperator.Equals, "$0")]
                    ),
                ],
                SignalVocabularyFixture.Real
            )
            .Should()
            .Be("filter_reference_fact_not_captured");
    }

    [Fact]
    public void The_editor_accepts_the_operators_own_example()
    {
        RewardTrackSequenceRules
            .FirstProblem(
                [
                    new RewardTrackTaskStepSpec(
                        RewardTrackActions.PlaceItem,
                        [
                            new(
                                RewardTrackFacts.Placement,
                                StepFilterOperator.Equals,
                                RewardTrackFacts.PlacementFloor
                            ),
                        ]
                    ),
                    new RewardTrackTaskStepSpec(
                        RewardTrackActions.WalkOnFurni,
                        [new(RewardTrackFacts.Item, StepFilterOperator.Equals, "$0")]
                    ),
                    new RewardTrackTaskStepSpec(
                        RewardTrackActions.PickUpItem,
                        [new(RewardTrackFacts.Item, StepFilterOperator.Equals, "$0")]
                    ),
                ],
                SignalVocabularyFixture.Real
            )
            .Should()
            .BeNull();
    }

    [Theory]
    // A number the operator typed as words. The kind check passes -- price really is a number --
    // so the only thing standing between this and a task that never advances is the literal check.
    [InlineData(
        RewardTrackActions.BuyClub,
        RewardTrackFacts.Price,
        StepFilterOperator.AtLeast,
        "cent",
        "filter_needs_a_number"
    )]
    [InlineData(
        RewardTrackActions.BuyClub,
        RewardTrackFacts.Price,
        StepFilterOperator.AtMost,
        "",
        "filter_incomplete"
    )]
    // The negated list has the same one-entry mistake the list itself has.
    [InlineData(
        RewardTrackActions.EnterOtherUsersRoom,
        RewardTrackFacts.Room,
        StepFilterOperator.NotOneOf,
        "500",
        "filter_one_of_needs_a_list"
    )]
    // Ordering over an id is nonsense, and the kind table is what refuses it.
    [InlineData(
        RewardTrackActions.EnterOtherUsersRoom,
        RewardTrackFacts.Room,
        StepFilterOperator.AtLeast,
        "500",
        "filter_operator_not_valid_for_fact"
    )]
    public void The_editor_refuses_a_new_operator_pointed_at_the_wrong_thing(
        string action,
        string factKey,
        StepFilterOperator op,
        string value,
        string expected
    ) =>
        RewardTrackSequenceRules
            .FirstProblem(
                [new RewardTrackTaskStepSpec(action, [new(factKey, op, value)])],
                SignalVocabularyFixture.Real
            )
            .Should()
            .Be(expected);

    [Fact]
    public void The_editor_accepts_barefoot_in_front_of_a_piece_of_furniture()
    {
        // The shape this was built for: get barefoot, then stand on that sofa. The absence of a
        // figure part has no value to match on, so NotContains is the only way to write it.
        RewardTrackSequenceRules
            .FirstProblem(
                [
                    new RewardTrackTaskStepSpec(
                        RewardTrackActions.ChangeFigure,
                        [new(RewardTrackFacts.Figure, StepFilterOperator.NotContains, "sh-")]
                    ),
                    new RewardTrackTaskStepSpec(
                        RewardTrackActions.WalkOnFurni,
                        [new(RewardTrackFacts.Definition, StepFilterOperator.Equals, "500")]
                    ),
                ],
                SignalVocabularyFixture.Real
            )
            .Should()
            .BeNull();
    }
}
