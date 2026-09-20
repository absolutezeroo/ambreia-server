using System.Linq;
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

    private static RewardTrackStepNodeSpec Leaf(
        string factKey,
        StepFilterOperator op,
        string value
    ) => new(PredicateKind.Test, new RewardTrackStepFilterSpec(factKey, op, value));

    /// <summary>A condition that saves today, used wherever a branch only has to be valid.</summary>
    private static RewardTrackStepNodeSpec Sound() =>
        Leaf(RewardTrackFacts.Definition, StepFilterOperator.Equals, "500");

    private static RewardTrackStepNodeSpec Group(
        PredicateKind kind,
        params RewardTrackStepNodeSpec[] children
    ) => new(kind, null, children);

    private static string? Refuses(params RewardTrackStepNodeSpec[] tree) =>
        RewardTrackSequenceRules.FirstProblem(
            [new RewardTrackTaskStepSpec(RewardTrackActions.WalkOnFurni, [], tree)],
            SignalVocabularyFixture.Real
        );

    [Fact]
    public void A_tree_of_sound_conditions_saves()
    {
        // "that sofa, and either not on the floor or in room 12" -- the shape a flat list cannot
        // express, and the reason any of this exists.
        Refuses(
                Sound(),
                Group(
                    PredicateKind.Any,
                    Group(
                        PredicateKind.Not,
                        Leaf(RewardTrackFacts.Definition, StepFilterOperator.Equals, "900")
                    ),
                    Leaf(RewardTrackFacts.Room, StepFilterOperator.Equals, "12")
                )
            )
            .Should()
            .BeNull();
    }

    [Fact]
    public void A_condition_buried_in_a_tree_is_judged_like_any_other()
    {
        // The rule that actually pays for this recursion: every per-condition refusal has to reach
        // a condition three levels down, or nesting would be a way to smuggle a dead task past the
        // editor.
        Refuses(
                Group(
                    PredicateKind.Any,
                    Sound(),
                    Group(
                        PredicateKind.All,
                        Group(
                            PredicateKind.Not,
                            Leaf(RewardTrackFacts.Item, StepFilterOperator.Equals, "$0")
                        )
                    )
                )
            )
            .Should()
            .Be("filter_reference_must_be_earlier");
    }

    [Theory]
    // A negation of nothing, and a negation of two things. Neither is a negation.
    [InlineData(PredicateKind.Not, 0, "predicate_not_needs_one_child")]
    [InlineData(PredicateKind.Not, 2, "predicate_not_needs_one_child")]
    // An empty Any is satisfied by nothing, and an empty All under one makes its whole branch true
    // without saying anything. Both save cleanly and both are content nobody meant to write.
    [InlineData(PredicateKind.Any, 0, "predicate_group_needs_a_child")]
    [InlineData(PredicateKind.All, 0, "predicate_group_needs_a_child")]
    [InlineData(PredicateKind.Any, 1, null)]
    [InlineData(PredicateKind.All, 2, null)]
    public void A_group_is_refused_unless_it_holds_what_it_can_use(
        PredicateKind kind,
        int children,
        string? expected
    ) =>
        Refuses(Group(kind, [.. Enumerable.Range(0, children).Select(_ => Sound())]))
            .Should()
            .Be(expected);

    [Fact]
    public void A_node_that_is_both_a_test_and_a_group_is_refused()
    {
        // Unreachable from the editor and reachable from the operations API, where the half the
        // engine ignores would look every bit as saved as the half it reads.
        Refuses(new RewardTrackStepNodeSpec(PredicateKind.Test, Sound().Test, [Sound()]))
            .Should()
            .Be("predicate_shape_mismatch");

        Refuses(new RewardTrackStepNodeSpec(PredicateKind.All, Sound().Test, [Sound()]))
            .Should()
            .Be("predicate_shape_mismatch");

        // A kind outside the enum. The engine reads it as false, so the task would never advance.
        Refuses(Group((PredicateKind)9, Sound())).Should().Be("predicate_shape_mismatch");
    }

    [Fact]
    public void A_test_with_nothing_in_it_is_the_same_mistake_nested_as_flat() =>
        Refuses(new RewardTrackStepNodeSpec(PredicateKind.Test, null, null))
            .Should()
            .Be("filter_incomplete");

    [Fact]
    public void The_arranged_conditions_are_the_ones_judged()
    {
        RewardTrackStepFilterSpec broken = new(
            RewardTrackFacts.Definition,
            StepFilterOperator.Equals,
            string.Empty
        );

        // The engine reads the tree and ignores the list, so the validator has to agree with it in
        // both directions: a list nobody reads cannot block a save, and it cannot wave one through.
        RewardTrackSequenceRules
            .FirstProblem(
                [new RewardTrackTaskStepSpec(RewardTrackActions.WalkOnFurni, [broken], [Sound()])],
                SignalVocabularyFixture.Real
            )
            .Should()
            .BeNull();

        RewardTrackSequenceRules
            .FirstProblem(
                [
                    new RewardTrackTaskStepSpec(
                        RewardTrackActions.WalkOnFurni,
                        [Sound().Test!],
                        [new RewardTrackStepNodeSpec(PredicateKind.Test, broken, null)]
                    ),
                ],
                SignalVocabularyFixture.Real
            )
            .Should()
            .Be("filter_incomplete");
    }

    [Fact]
    public void An_empty_tree_is_a_step_with_no_conditions_not_a_refusal() =>
        RewardTrackSequenceRules
            .FirstProblem(
                [new RewardTrackTaskStepSpec(RewardTrackActions.WalkOnFurni, [], [])],
                SignalVocabularyFixture.Real
            )
            .Should()
            .BeNull();
}
