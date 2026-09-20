using System.Collections.Immutable;
using System.Linq;
using FluentAssertions;
using Vortex.Primitives.RewardTracks;
using Vortex.Primitives.RewardTracks.Snapshots;
using Vortex.RewardTracks.Progression;
using Xunit;

namespace Vortex.Rewards.Tests;

/// <summary>
/// Both sides of a comparison, and what that lets a condition ask.
/// </summary>
/// <remarks>
/// A test used to be one fact against something typed. The first thing this has to prove is that
/// it still is, for every task written that way — which is every task that exists — and the second
/// is that the questions the old shape could not ask are now asked correctly rather than merely
/// accepted.
/// </remarks>
public sealed class StepValueTests
{
    private static RewardTrackStepFilterSnapshot Test(
        StepValueSnapshot left,
        StepFilterOperator op,
        StepValueSnapshot right
    ) =>
        new()
        {
            // The three legacy fields stay filled and stay ignored: a filter carrying both sides
            // must read them, not these, or the fallback would be hiding a broken new path.
            FactKey = "unused",
            Operator = op,
            Value = "unused",
            ReferencedStep = -1,
            Left = left,
            Right = right,
        };

    private static StepValueSnapshot Fact(string key) => StepValueSnapshot.OfFact(key);

    private static StepValueSnapshot Literal(string text) => StepValueSnapshot.OfLiteral(text);

    private static StepValueSnapshot Count(string key) =>
        new() { Kind = StepValueKind.CountOf, Arg = key };

    private static RewardTrackTaskStepSnapshot Step(
        string action,
        params RewardTrackStepFilterSnapshot[] filters
    ) =>
        new()
        {
            StepIndex = 0,
            ActionCode = action,
            Filters = [.. filters],
        };

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

    [Theory]
    // The question a flat test cannot ask at all: two facts of the same signal, against each other.
    [InlineData("7", "7", true)]
    [InlineData("7", "9", false)]
    public void A_fact_can_be_compared_with_another_fact(
        string owner,
        string player,
        bool expected
    ) =>
        Matches(
                Step(
                    RewardTrackActions.EnterOtherUsersRoom,
                    Test(
                        Fact(RewardTrackFacts.RoomOwner),
                        StepFilterOperator.Equals,
                        Fact(RewardTrackFacts.Player)
                    )
                ),
                (RewardTrackFacts.RoomOwner, owner),
                (RewardTrackFacts.Player, player)
            )
            .Should()
            .Be(expected);

    [Theory]
    // "wearing at least three things". The fact is a list, and the question is about how long it
    // is -- which no operator over its contents can answer.
    [InlineData("hr-1,ch-2,lg-3", true)]
    [InlineData("hr-1,ch-2", false)]
    // Trailing and doubled separators are what a picker leaves behind; neither is an entry.
    [InlineData("hr-1,ch-2,,lg-3,", true)]
    [InlineData("", false)]
    public void A_list_fact_can_be_counted(string worn, bool expected) =>
        Matches(
                Step(
                    RewardTrackActions.ChangeFigure,
                    Test(
                        Count(RewardTrackFacts.FigureSet),
                        StepFilterOperator.AtLeast,
                        Literal("3")
                    )
                ),
                (RewardTrackFacts.FigureSet, worn)
            )
            .Should()
            .Be(expected);

    [Fact]
    public void A_literal_can_be_on_the_left()
    {
        // Falls out of both sides being values, and is worth pinning because the old shape made
        // the left side structurally a fact -- there was no way to write this.
        Matches(
                Step(
                    RewardTrackActions.EnterOtherUsersRoom,
                    Test(Literal("12"), StepFilterOperator.Equals, Fact(RewardTrackFacts.Room))
                ),
                (RewardTrackFacts.Room, "12")
            )
            .Should()
            .BeTrue();
    }

    [Theory]
    // A side naming something the signal does not carry has not made anything true -- including a
    // NotEquals, which is a claim about a value, and including the side that used to be the
    // literal and so could never be absent.
    [InlineData(StepFilterOperator.Equals)]
    [InlineData(StepFilterOperator.NotEquals)]
    public void A_missing_fact_fails_on_either_side(StepFilterOperator op)
    {
        Matches(
                Step(
                    RewardTrackActions.EnterOtherUsersRoom,
                    Test(Fact(RewardTrackFacts.RoomOwner), op, Fact(RewardTrackFacts.Player))
                ),
                (RewardTrackFacts.Player, "7")
            )
            .Should()
            .BeFalse();

        Matches(
                Step(
                    RewardTrackActions.EnterOtherUsersRoom,
                    Test(Fact(RewardTrackFacts.RoomOwner), op, Fact(RewardTrackFacts.Player))
                ),
                (RewardTrackFacts.RoomOwner, "7")
            )
            .Should()
            .BeFalse();
    }

    [Fact]
    public void A_count_of_a_fact_that_is_absent_is_not_zero()
    {
        // Worth its own test because "zero" is the tempting answer and it is the wrong one: an
        // action that reports no garments has not reported that the player wears none. Read as
        // zero, "wearing at most two things" would be satisfied by every signal in the hotel.
        Matches(
                Step(
                    RewardTrackActions.ChangeFigure,
                    Test(Count(RewardTrackFacts.FigureSet), StepFilterOperator.AtMost, Literal("2"))
                ),
                (RewardTrackFacts.Figure, "hr-1")
            )
            .Should()
            .BeFalse();
    }

    [Fact]
    public void A_kind_this_build_does_not_know_stops_its_own_task_and_nothing_else()
    {
        // Content written by a newer build, read by this one. The room's event path carries every
        // other campaign in the hotel, so an unknown kind is false rather than a throw.
        Matches(
                Step(
                    RewardTrackActions.EnterOtherUsersRoom,
                    Test(
                        new StepValueSnapshot { Kind = (StepValueKind)77, Arg = "x" },
                        StepFilterOperator.Equals,
                        Literal("x")
                    )
                ),
                (RewardTrackFacts.Room, "12")
            )
            .Should()
            .BeFalse();
    }

    /// <summary>Walks a two-step task, threading what step 0 captured into step 1.</summary>
    private static bool Walks(
        RewardTrackTaskStepSnapshot first,
        RewardTrackTaskStepSnapshot second,
        (string Key, string Value)[] firstFacts,
        (string Key, string Value)[] secondFacts
    )
    {
        RewardTrackTaskDefinitionSnapshot task = Content.Sequence(
            steps: [first with { StepIndex = 0 }, second with { StepIndex = 1 }]
        );

        TaskProgressOutcome after = TaskProgressRules.Apply(
            task,
            0,
            -1,
            string.Empty,
            0,
            string.Empty,
            first.ActionCode,
            1,
            null,
            [.. firstFacts.Select(f => new RewardTrackFactSnapshot(f.Key, f.Value))],
            premiumUnlocked: false
        );

        return TaskProgressRules
                .Apply(
                    task,
                    after.NewProgress,
                    after.HighestPaidLevelIndex,
                    after.DistinctKeys,
                    after.NewStep,
                    after.Captures,
                    second.ActionCode,
                    1,
                    null,
                    [.. secondFacts.Select(f => new RewardTrackFactSnapshot(f.Key, f.Value))],
                    premiumUnlocked: false
                )
                .NewProgress > 0;
    }

    [Theory]
    // "talk to someone, then walk into a room THEY own." The two steps name different facts --
    // the player who spoke, and the owner of a room -- which is exactly what $N cannot say: it
    // resolves an earlier step's value for the SAME key, so it could only ever compare a player
    // with a player.
    [InlineData("7", "7", true)]
    [InlineData("7", "9", false)]
    public void An_earlier_step_can_be_read_under_a_different_fact(
        string spokeTo,
        string owner,
        bool expected
    ) =>
        Walks(
                Step(RewardTrackActions.ChatWithSomeone),
                Step(
                    RewardTrackActions.EnterOtherUsersRoom,
                    Test(
                        Fact(RewardTrackFacts.RoomOwner),
                        StepFilterOperator.Equals,
                        StepValueSnapshot.OfStep(0, RewardTrackFacts.Target)
                    )
                ),
                [(RewardTrackFacts.Target, spokeTo)],
                [(RewardTrackFacts.RoomOwner, owner)]
            )
            .Should()
            .Be(expected);

    [Fact]
    public void An_earlier_step_that_recorded_nothing_satisfies_nothing() =>
        Walks(
                Step(RewardTrackActions.ChatWithSomeone),
                Step(
                    RewardTrackActions.EnterOtherUsersRoom,
                    Test(
                        Fact(RewardTrackFacts.RoomOwner),
                        StepFilterOperator.Equals,
                        StepValueSnapshot.OfStep(0, RewardTrackFacts.Target)
                    )
                ),
                // Step 0 is satisfied -- it has no conditions -- but it captured no target, so
                // there is nothing for step 1 to compare against.
                [(RewardTrackFacts.Room, "12")],
                [(RewardTrackFacts.RoomOwner, "7")]
            )
            .Should()
            .BeFalse();

    [Fact]
    public void A_filter_with_no_sides_is_read_exactly_as_it_always_was()
    {
        // The compatibility claim, stated as a test. Every filter stored today has both sides null,
        // and must go on meaning "my own fact, against my own literal".
        RewardTrackStepFilterSnapshot legacy = new()
        {
            FactKey = RewardTrackFacts.Room,
            Operator = StepFilterOperator.Equals,
            Value = "12",
            ReferencedStep = -1,
        };

        Matches(Step(RewardTrackActions.EnterOtherUsersRoom, legacy), (RewardTrackFacts.Room, "12"))
            .Should()
            .BeTrue();

        Matches(Step(RewardTrackActions.EnterOtherUsersRoom, legacy), (RewardTrackFacts.Room, "99"))
            .Should()
            .BeFalse();
    }
}
