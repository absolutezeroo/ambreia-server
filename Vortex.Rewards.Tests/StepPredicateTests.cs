using System.Collections.Immutable;
using System.Linq;
using FluentAssertions;
using Vortex.Primitives.RewardTracks;
using Vortex.Primitives.RewardTracks.Snapshots;
using Vortex.RewardTracks.Progression;
using Xunit;

namespace Vortex.Rewards.Tests;

/// <summary>
/// A step's conditions as a tree, and the flat list every task still carries.
/// </summary>
/// <remarks>
/// This decides who is paid, so the first thing it has to prove is that it changed nothing: a flat
/// list of filters and an <c>All</c> of the same tests are the same question, and every task
/// written so far asks it the flat way.
/// </remarks>
public sealed class StepPredicateTests
{
    private static RewardTrackStepFilterSnapshot Test(string key, string value) =>
        new()
        {
            FactKey = key,
            Operator = StepFilterOperator.Equals,
            Value = value,
            ReferencedStep = -1,
        };

    private static StepPredicateSnapshot Leaf(string key, string value) =>
        new() { Kind = PredicateKind.Test, Test = Test(key, value) };

    private static StepPredicateSnapshot Node(
        PredicateKind kind,
        params StepPredicateSnapshot[] children
    ) => new() { Kind = kind, Children = [.. children] };

    /// <summary>Whether a signal carrying these facts satisfies a one-step task.</summary>
    private static bool Matches(
        RewardTrackTaskStepSnapshot step,
        params (string Key, string Value)[] facts
    ) =>
        TaskProgressRules
            .Apply(
                Content.Sequence(steps: [step with { StepIndex = 0 }]),
                0,
                -1,
                string.Empty,
                0,
                string.Empty,
                step.ActionCode,
                1,
                null,
                [.. facts.Select(f => new RewardTrackFactSnapshot(f.Key, f.Value))],
                premiumUnlocked: false
            )
            .NewProgress > 0;

    private static RewardTrackTaskStepSnapshot Flat(params RewardTrackStepFilterSnapshot[] tests) =>
        new()
        {
            StepIndex = 0,
            ActionCode = RewardTrackActions.EnterOtherUsersRoom,
            Filters = [.. tests],
        };

    private static RewardTrackTaskStepSnapshot Tree(StepPredicateSnapshot root) =>
        new()
        {
            StepIndex = 0,
            ActionCode = RewardTrackActions.EnterOtherUsersRoom,
            Root = root,
        };

    [Theory]
    [InlineData("12", "casino", true)]
    [InlineData("12", "other", false)]
    [InlineData("99", "casino", false)]
    public void A_flat_list_and_an_All_of_the_same_tests_answer_alike(
        string room,
        string name,
        bool expected
    )
    {
        // The compatibility claim, stated as a test rather than as a comment: nothing written
        // before trees existed may change its answer because trees now exist.
        (string, string)[] facts =
        [
            (RewardTrackFacts.Room, room),
            (RewardTrackFacts.RoomName, name),
        ];

        RewardTrackStepFilterSnapshot[] tests =
        [
            Test(RewardTrackFacts.Room, "12"),
            Test(RewardTrackFacts.RoomName, "casino"),
        ];

        Matches(Flat(tests), facts).Should().Be(expected);
        Matches(
                Tree(
                    Node(
                        PredicateKind.All,
                        Leaf(RewardTrackFacts.Room, "12"),
                        Leaf(RewardTrackFacts.RoomName, "casino")
                    )
                ),
                facts
            )
            .Should()
            .Be(expected);
    }

    [Fact]
    public void No_conditions_at_all_still_means_any_occurrence_counts()
    {
        // Both spellings of "nothing is required". The empty All is the one that would silently
        // stop paying everybody if it were read as false.
        Matches(Flat()).Should().BeTrue();
        Matches(Tree(Node(PredicateKind.All))).Should().BeTrue();
    }

    [Fact]
    public void One_of_nothing_satisfies_nothing()
    {
        // The mirror, and the one that would silently pay everybody. An Any with no children is
        // content nobody meant to write, and it must not read as "no conditions".
        Matches(Tree(Node(PredicateKind.Any))).Should().BeFalse();
    }

    [Theory]
    [InlineData("12", true)]
    [InlineData("34", true)]
    [InlineData("99", false)]
    public void Any_holds_when_one_branch_does(string room, bool expected) =>
        Matches(
                Tree(
                    Node(
                        PredicateKind.Any,
                        Leaf(RewardTrackFacts.Room, "12"),
                        Leaf(RewardTrackFacts.Room, "34")
                    )
                ),
                (RewardTrackFacts.Room, room)
            )
            .Should()
            .Be(expected);

    [Theory]
    [InlineData("12", false)]
    [InlineData("99", true)]
    public void Not_inverts_its_child(string room, bool expected) =>
        Matches(
                Tree(Node(PredicateKind.Not, Leaf(RewardTrackFacts.Room, "12"))),
                (RewardTrackFacts.Room, room)
            )
            .Should()
            .Be(expected);

    [Fact]
    public void Not_over_a_missing_fact_is_true_which_is_not_what_NotEquals_does()
    {
        // Worth pinning because the two read alike and are not alike. A NotEquals on a fact the
        // signal never carried FAILS -- "any room but yours" is a claim about a room. A Not around
        // that same test SUCCEEDS, because the inner test failed and Not inverts it.
        //
        // Both are defensible; what matters is that an operator gets the one the picture shows.
        Matches(Tree(Node(PredicateKind.Not, Leaf(RewardTrackFacts.Room, "12"))))
            .Should()
            .BeTrue();

        Matches(
                Flat(
                    new RewardTrackStepFilterSnapshot
                    {
                        FactKey = RewardTrackFacts.Room,
                        Operator = StepFilterOperator.NotEquals,
                        Value = "12",
                        ReferencedStep = -1,
                    }
                )
            )
            .Should()
            .BeFalse();
    }

    [Fact]
    public void Nesting_goes_as_deep_as_the_content_does()
    {
        // "room 12, and either the casino or anything not called lobby" -- the shape a flat list
        // cannot express at all, which is the whole reason for this.
        StepPredicateSnapshot root = Node(
            PredicateKind.All,
            Leaf(RewardTrackFacts.Room, "12"),
            Node(
                PredicateKind.Any,
                Leaf(RewardTrackFacts.RoomName, "casino"),
                Node(PredicateKind.Not, Leaf(RewardTrackFacts.RoomName, "lobby"))
            )
        );

        Matches(Tree(root), (RewardTrackFacts.Room, "12"), (RewardTrackFacts.RoomName, "casino"))
            .Should()
            .BeTrue();

        Matches(Tree(root), (RewardTrackFacts.Room, "12"), (RewardTrackFacts.RoomName, "arcade"))
            .Should()
            .BeTrue();

        Matches(Tree(root), (RewardTrackFacts.Room, "12"), (RewardTrackFacts.RoomName, "lobby"))
            .Should()
            .BeFalse();

        Matches(Tree(root), (RewardTrackFacts.Room, "99"), (RewardTrackFacts.RoomName, "casino"))
            .Should()
            .BeFalse();
    }

    [Theory]
    // A Not with no child, and a Not with two: neither is a negation of anything.
    [InlineData(PredicateKind.Not, 0)]
    [InlineData(PredicateKind.Not, 2)]
    public void A_malformed_node_stops_its_own_task_and_nothing_else(
        PredicateKind kind,
        int children
    )
    {
        StepPredicateSnapshot[] kids =
        [
            .. Enumerable.Range(0, children).Select(i => Leaf(RewardTrackFacts.Room, "12")),
        ];

        Matches(Tree(Node(kind, kids)), (RewardTrackFacts.Room, "12")).Should().BeFalse();
    }

    [Fact]
    public void A_test_node_with_no_test_is_false_rather_than_a_throw()
    {
        // Content the validator should have refused. It reaches here from the operations API or a
        // hand-edited row, and on the room's event path a throw would take the pipeline with it.
        Matches(Tree(new StepPredicateSnapshot { Kind = PredicateKind.Test })).Should().BeFalse();
    }
}
