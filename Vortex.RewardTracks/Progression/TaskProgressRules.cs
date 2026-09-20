using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using Vortex.Primitives.RewardTracks;
using Vortex.Primitives.RewardTracks.Snapshots;

namespace Vortex.RewardTracks.Progression;

/// <summary>What one progress signal did to one task.</summary>
/// <param name="NewProgress">The task's progress count afterwards.</param>
/// <param name="StagesPaid">
/// The stages that were reached for the first time, in order. Empty when the signal moved the
/// count without finishing anything, which is the common case.
/// </param>
/// <param name="PointsGranted">Track points the stages paid, before any premium boost.</param>
/// <param name="HighestPaidLevelIndex">The new watermark to store.</param>
/// <param name="DistinctKeys">The distinct-key set to store, unchanged for non-distinct tasks.</param>
/// <param name="NewStep">
/// Where the player now stands in the task's sequence. Always zero for a plain task, which is a
/// sequence of one and so is never mid-flight.
/// </param>
/// <param name="Captures">
/// What each satisfied step matched, so a later step can point back at it. Cleared whenever the
/// sequence completes.
/// </param>
public readonly record struct TaskProgressOutcome(
    int NewProgress,
    ImmutableArray<int> StagesPaid,
    int PointsGranted,
    int HighestPaidLevelIndex,
    string DistinctKeys,
    int NewStep = 0,
    string Captures = ""
)
{
    /// <summary>Nothing happened: the signal did not apply, or it changed nothing worth storing.</summary>
    public static TaskProgressOutcome None(
        int progress,
        int watermark,
        string distinctKeys,
        int step = 0,
        string captures = ""
    ) => new(progress, [], 0, watermark, distinctKeys, step, captures);

    /// <summary>
    /// Whether anything needs persisting or telling the client about. Moving one step along a
    /// sequence counts even though the count did not change: losing that cursor would put a player
    /// back at the start of a task they were halfway through.
    /// </summary>
    public bool Changed(int previousProgress, int previousStep = 0) =>
        NewProgress != previousProgress || NewStep != previousStep || !StagesPaid.IsDefaultOrEmpty;
}

/// <summary>
/// How a signal turns into progress, and how progress turns into paid stages.
/// </summary>
/// <remarks>
/// <para>
/// Pure: definition in, stored state in, an outcome out. No clock, no database, no grain. Every
/// rule about double-paying, about which stages a jump past three thresholds pays, and about how
/// each mode counts, lives here and is directly testable — which matters more here than almost
/// anywhere, because the failure mode is silently paying a player twice.
/// </para>
/// <para>
/// There is no switch on a task <em>type</em> anywhere. There are four modes, and a mode decides
/// only how the count moves; everything after that is the same code for every task in the hotel.
/// </para>
/// </remarks>
internal static class TaskProgressRules
{
    private const char DistinctSeparator = '\t';

    /// <summary>
    /// Applies one signal.
    /// </summary>
    /// <param name="amount">
    /// The increment for <see cref="TaskProgressMode.Counter"/>, or the reported total for
    /// <see cref="TaskProgressMode.Absolute"/> and <see cref="TaskProgressMode.Highest"/>. Ignored
    /// by <see cref="TaskProgressMode.Distinct"/>, which counts keys rather than amounts.
    /// </param>
    /// <param name="target">
    /// What the signal was about. Must match the task's <c>Parameter</c> when it has one, and is
    /// the dedup key for a distinct task.
    /// </param>
    /// <param name="premiumUnlocked">
    /// Whether the player holds premium on this track. Premium stages are neither reached nor paid
    /// without it — a free player's progress still climbs past them, so buying premium later pays
    /// what they had already earned.
    /// </param>
    /// <summary>
    /// The plain-task shorthand: one signal, of this task's own action, carrying no facts and with
    /// no sequence in flight. What almost every task is, and what every caller outside a sequence
    /// means.
    /// </summary>
    public static TaskProgressOutcome Apply(
        RewardTrackTaskDefinitionSnapshot task,
        int currentProgress,
        int highestPaidLevelIndex,
        string distinctKeys,
        int amount,
        string? target,
        bool premiumUnlocked
    ) =>
        Apply(
            task,
            currentProgress,
            highestPaidLevelIndex,
            distinctKeys,
            0,
            string.Empty,
            task.Steps.IsDefaultOrEmpty ? task.ActionCode : task.Steps[0].ActionCode,
            amount,
            target,
            target is null ? [] : [new RewardTrackFactSnapshot(RewardTrackFacts.Target, target)],
            premiumUnlocked
        );

    public static TaskProgressOutcome Apply(
        RewardTrackTaskDefinitionSnapshot task,
        int currentProgress,
        int highestPaidLevelIndex,
        string distinctKeys,
        int currentStep,
        string capturedFacts,
        string actionCode,
        int amount,
        string? target,
        ImmutableArray<RewardTrackFactSnapshot> facts,
        bool premiumUnlocked
    )
    {
        TaskProgressOutcome Unchanged() =>
            TaskProgressOutcome.None(
                currentProgress,
                highestPaidLevelIndex,
                distinctKeys,
                currentStep,
                capturedFacts
            );

        if (task.Levels.IsDefaultOrEmpty || task.Steps.IsDefaultOrEmpty)
        {
            return Unchanged();
        }

        if (task.Premium && !premiumUnlocked)
        {
            // A premium-only task does not advance at all for a free player. Letting it climb
            // invisibly and pay out the moment premium was bought would be a second, hidden
            // retroactive grant on top of the one that is deliberate.
            return Unchanged();
        }

        int step = Math.Clamp(currentStep, 0, task.Steps.Length - 1);
        RewardTrackTaskStepSnapshot current = task.Steps[step];

        // A sequence is woken by every action any of its steps names, so all but one of those
        // wake-ups are for a step the player is not standing on.
        if (!string.Equals(current.ActionCode, actionCode, StringComparison.Ordinal))
        {
            return Unchanged();
        }

        // The task's own parameter is step 0's target filter. Kept rather than folded into the
        // step's filters because it is on the wire and the client reads it.
        if (
            step == 0
            && task.Parameter.Length > 0
            && !string.Equals(task.Parameter, target, StringComparison.Ordinal)
        )
        {
            return Unchanged();
        }

        StepCaptures captures = StepCaptures.Parse(capturedFacts);

        if (!StepMatches(current, facts, captures))
        {
            return Unchanged();
        }

        // An action that is not the one being waited for never resets the sequence. "Talk, then add
        // a friend" has to survive the fifty other things a player does in between; punishing
        // ordinary play would make every multi-step task read as broken.
        if (step + 1 < task.Steps.Length)
        {
            return new TaskProgressOutcome(
                currentProgress,
                [],
                0,
                highestPaidLevelIndex,
                distinctKeys,
                step + 1,
                captures.With(step, facts).Serialize()
            );
        }

        (int newProgress, string newKeys) = Advance(
            task,
            currentProgress,
            distinctKeys,
            amount,
            target
        );

        // The sequence restarts either way. Landing the last step is what completes it, even when
        // the count did not move -- a distinct task on a key already seen pays nothing and still
        // has to be walked again from the top.
        if (newProgress == currentProgress)
        {
            return TaskProgressOutcome.None(
                currentProgress,
                highestPaidLevelIndex,
                distinctKeys,
                0,
                string.Empty
            );
        }

        (ImmutableArray<int> paid, int points, int watermark) = PayStages(
            task,
            newProgress,
            highestPaidLevelIndex,
            premiumUnlocked
        );

        return new TaskProgressOutcome(
            newProgress,
            paid,
            points,
            watermark,
            newKeys,
            0,
            string.Empty
        );
    }

    /// <summary>
    /// Writes a progress count directly, as the wired <c>PROGRESS_REWARD_TRACK</c> action does. Pays
    /// whatever stages the new count reaches, by the same watermark rule as everything else.
    /// </summary>
    public static TaskProgressOutcome Set(
        RewardTrackTaskDefinitionSnapshot task,
        int highestPaidLevelIndex,
        string distinctKeys,
        int newProgress,
        bool premiumUnlocked
    )
    {
        if (task.Levels.IsDefaultOrEmpty || (task.Premium && !premiumUnlocked))
        {
            return TaskProgressOutcome.None(newProgress, highestPaidLevelIndex, distinctKeys);
        }

        int clamped = Math.Clamp(newProgress, 0, task.MaxRequiredCount);

        (ImmutableArray<int> paid, int points, int watermark) = PayStages(
            task,
            clamped,
            highestPaidLevelIndex,
            premiumUnlocked
        );

        return new TaskProgressOutcome(clamped, paid, points, watermark, distinctKeys);
    }

    /// <summary>Whether the task has reached its last stage.</summary>
    public static bool IsComplete(RewardTrackTaskDefinitionSnapshot task, int progress) =>
        !task.Levels.IsDefaultOrEmpty && progress >= task.MaxRequiredCount;

    /// <summary>
    /// Moves the count by the mode's own rule, and returns the distinct-key set to store with it.
    /// </summary>
    private static (int Progress, string Keys) Advance(
        RewardTrackTaskDefinitionSnapshot task,
        int currentProgress,
        string distinctKeys,
        int amount,
        string? target
    )
    {
        int max = task.MaxRequiredCount;

        switch (task.Mode)
        {
            case TaskProgressMode.Counter:
                return (Math.Min(max, currentProgress + Math.Max(1, amount)), distinctKeys);

            case TaskProgressMode.Absolute:
                // The world reported a total. It may be lower than last time -- unfriending someone
                // really does take a "have 5 friends" task back down -- and that is the mode's whole
                // point. Stages already paid stay paid; the watermark is what protects them.
                return (Math.Clamp(amount, 0, max), distinctKeys);

            case TaskProgressMode.Highest:
                return (Math.Clamp(Math.Max(currentProgress, amount), 0, max), distinctKeys);

            case TaskProgressMode.Distinct:
                return AdvanceDistinct(currentProgress, distinctKeys, target, max);

            default:
                return (currentProgress, distinctKeys);
        }
    }

    /// <summary>
    /// Counts a key not seen before, once.
    /// </summary>
    /// <remarks>
    /// The set stops growing at the task's own maximum: past that the count cannot move, so there
    /// is nothing left to deduplicate against and no reason to keep remembering. That is what bounds
    /// the column by the content rather than by how long someone plays.
    /// </remarks>
    private static (int Progress, string Keys) AdvanceDistinct(
        int currentProgress,
        string distinctKeys,
        string? target,
        int max
    )
    {
        if (string.IsNullOrEmpty(target) || currentProgress >= max)
        {
            return (currentProgress, distinctKeys);
        }

        foreach (string key in Split(distinctKeys))
        {
            if (string.Equals(key, target, StringComparison.Ordinal))
            {
                return (currentProgress, distinctKeys);
            }
        }

        string next = distinctKeys.Length == 0 ? target : distinctKeys + DistinctSeparator + target;

        return (Math.Min(max, currentProgress + 1), next);
    }

    /// <summary>
    /// Every stage the count now reaches that has not been paid yet.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The watermark, not the count, decides what is owed. A progress update that jumps past three
    /// thresholds at once pays all three; the same update arriving twice pays nothing the second
    /// time, because the watermark has already moved past them. That single rule is what makes a
    /// reconnect, a duplicated event and a retried grain call all harmless.
    /// </para>
    /// <para>
    /// A premium stage a free player has passed is skipped rather than paid — but the watermark is
    /// <em>not</em> advanced over it, so buying premium later pays it. That is the one place the
    /// engine looks backwards, and it is deliberate: the stage was earned, only the entitlement to
    /// collect it was missing.
    /// </para>
    /// </remarks>
    private static (ImmutableArray<int> Paid, int Points, int Watermark) PayStages(
        RewardTrackTaskDefinitionSnapshot task,
        int progress,
        int highestPaidLevelIndex,
        bool premiumUnlocked
    )
    {
        List<int> paid = [];
        int points = 0;
        int watermark = highestPaidLevelIndex;

        for (int i = 0; i < task.Levels.Length; i++)
        {
            RewardTrackTaskLevelSnapshot level = task.Levels[i];

            if (i <= highestPaidLevelIndex || progress < level.RequiredCount)
            {
                continue;
            }

            if (level.Premium && !premiumUnlocked)
            {
                // Leave the watermark where it is. Advancing it would write off a stage the player
                // reached, and buying premium afterwards would silently skip it.
                break;
            }

            paid.Add(i);
            points += level.PointsReward;
            watermark = i;
        }

        return ([.. paid], points, watermark);
    }

    /// <summary>
    /// Whether a signal satisfies the step the player is currently on.
    /// </summary>
    /// <remarks>
    /// Pure and total. An unresolvable back-reference or a fact the signal does not carry fails the
    /// step rather than throwing: this runs on the room's event path behind content an operator
    /// typed, and a campaign that stops the pipeline is worse than a task that does not advance.
    /// </remarks>
    private static bool StepMatches(
        RewardTrackTaskStepSnapshot step,
        ImmutableArray<RewardTrackFactSnapshot> facts,
        StepCaptures captures
    )
    {
        // A nested step carries a tree; every task written before trees existed carries the flat
        // list, and the two agree by construction -- "the filters are ANDed" is an All of tests.
        // Read as two paths rather than by wrapping the list, so nothing is rebuilt per signal for
        // the tasks that do not nest, which is all of them so far.
        if (step.Root is StepPredicateSnapshot root)
        {
            return Holds(root, facts, captures);
        }

        if (step.Filters.IsDefaultOrEmpty)
        {
            return true;
        }

        foreach (RewardTrackStepFilterSnapshot filter in step.Filters)
        {
            if (!TestHolds(filter, facts, captures))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether a node of a condition tree holds.
    /// </summary>
    /// <remarks>
    /// Recursive, and total for the same reason the rest of this file is: a malformed node — a
    /// <c>Not</c> with no child, a <c>Test</c> with no test — is content that should have been
    /// refused when it was written, and reading it as false stops one task rather than the
    /// pipeline.
    /// <para>
    /// An empty <c>All</c> holds and an empty <c>Any</c> does not. Those are not arbitrary: "no
    /// conditions" has always meant any occurrence counts, and "one of nothing" has satisfied
    /// nothing.
    /// </para>
    /// </remarks>
    private static bool Holds(
        StepPredicateSnapshot node,
        ImmutableArray<RewardTrackFactSnapshot> facts,
        StepCaptures captures
    )
    {
        switch (node.Kind)
        {
            case PredicateKind.Test:
                return node.Test is RewardTrackStepFilterSnapshot test
                    && TestHolds(test, facts, captures);

            case PredicateKind.All:
                foreach (StepPredicateSnapshot child in Operands(node))
                {
                    if (!Holds(child, facts, captures))
                    {
                        return false;
                    }
                }

                return true;

            case PredicateKind.Any:
                foreach (StepPredicateSnapshot child in Operands(node))
                {
                    if (Holds(child, facts, captures))
                    {
                        return true;
                    }
                }

                return false;

            case PredicateKind.Not:
                return Operands(node) is [StepPredicateSnapshot only]
                    && !Holds(only, facts, captures);

            default:
                return false;
        }
    }

    private static ImmutableArray<StepPredicateSnapshot> Operands(StepPredicateSnapshot node) =>
        node.Children.IsDefault ? [] : node.Children;

    /// <summary>Whether one comparison holds against what the signal carried.</summary>
    private static bool TestHolds(
        RewardTrackStepFilterSnapshot filter,
        ImmutableArray<RewardTrackFactSnapshot> facts,
        StepCaptures captures
    )
    {
        {
            if (LeftOf(filter, facts, captures) is not string actual)
            {
                // A side asks about something this signal does not carry. That includes
                // NotEquals: "any room but yours" is a claim about a room, and an action naming no
                // room has not made it true.
                return false;
            }

            // A step it points at has not run in this attempt, or a fact it names is absent.
            // Nothing to compare to, either way.
            if (RightOf(filter, facts, captures) is not string expected)
            {
                return false;
            }

            bool ok = filter.Operator switch
            {
                StepFilterOperator.Equals => string.Equals(
                    actual,
                    expected,
                    StringComparison.Ordinal
                ),
                StepFilterOperator.NotEquals => !string.Equals(
                    actual,
                    expected,
                    StringComparison.Ordinal
                ),
                StepFilterOperator.OneOf => ListContains(expected, actual),
                StepFilterOperator.NotOneOf => !ListContains(expected, actual),
                // Ordinal-ignore-case, not the current culture: the same content has to match the
                // same room names on every silo, whatever locale the process happens to run in.
                StepFilterOperator.Contains => actual.Contains(
                    expected,
                    StringComparison.OrdinalIgnoreCase
                ),
                StepFilterOperator.NotContains => !actual.Contains(
                    expected,
                    StringComparison.OrdinalIgnoreCase
                ),
                StepFilterOperator.AtLeast => Compare(actual, expected) is int atLeast
                    && atLeast >= 0,
                StepFilterOperator.AtMost => Compare(actual, expected) is int atMost && atMost <= 0,
                // The mirror of OneOf: here the fact is the list and the filter is what to look for
                // in it. A look wears several garments, so the question is membership in what the
                // signal carried.
                StepFilterOperator.HasAnyOf => ListsIntersect(actual, expected),
                StepFilterOperator.HasNoneOf => !ListsIntersect(actual, expected),
                _ => false,
            };

            return ok;
        }
    }

    /// <summary>
    /// The left side of a comparison, or <c>null</c> when this signal cannot supply it.
    /// </summary>
    /// <remarks>
    /// The fallback is the whole compatibility story and is deliberately the old code, not a
    /// rewrite of it: a filter with no <see cref="RewardTrackStepFilterSnapshot.Left"/> asks about
    /// its own fact, which is what the left side has meant since this table existed.
    /// </remarks>
    private static string? LeftOf(
        RewardTrackStepFilterSnapshot filter,
        ImmutableArray<RewardTrackFactSnapshot> facts,
        StepCaptures captures
    ) =>
        filter.Left is StepValueSnapshot left
            ? Resolve(left, facts, captures)
            : Fact(facts, filter.FactKey);

    /// <summary>The right side, or <c>null</c> when nothing can supply it.</summary>
    private static string? RightOf(
        RewardTrackStepFilterSnapshot filter,
        ImmutableArray<RewardTrackFactSnapshot> facts,
        StepCaptures captures
    ) =>
        filter.Right is StepValueSnapshot right ? Resolve(right, facts, captures)
        : filter.ReferencedStep < 0 ? filter.Value
        : captures.Get(filter.ReferencedStep, filter.FactKey);

    /// <summary>
    /// What one side is worth against this signal, or <c>null</c> when the signal does not carry it.
    /// </summary>
    /// <remarks>
    /// Every branch is pure and allocation-free but for the count, which formats one small number.
    /// A kind that had to look anything up would run here — once per signal, per player in the
    /// room — which is why no kind does. See <see cref="StepValueKind"/>.
    /// </remarks>
    private static string? Resolve(
        StepValueSnapshot value,
        ImmutableArray<RewardTrackFactSnapshot> facts,
        StepCaptures captures
    ) =>
        value.Kind switch
        {
            StepValueKind.Fact => Fact(facts, value.Arg),
            StepValueKind.Literal => value.Arg,
            StepValueKind.StepFact => captures.Get(value.Step, value.Arg),
            StepValueKind.CountOf => Fact(facts, value.Arg) is string list
                ? Entries(list).ToString(CultureInfo.InvariantCulture)
                : null,
            // A kind this build does not know, from content written by a newer one. False rather
            // than a throw: the room's event path carries every other campaign too.
            _ => null,
        };

    /// <summary>How many entries a comma-separated fact carried. An empty fact carries none.</summary>
    private static int Entries(string list)
    {
        int entries = 0;

        foreach (Range part in list.AsSpan().Split(','))
        {
            if (!list.AsSpan()[part].Trim().IsEmpty)
            {
                entries++;
            }
        }

        return entries;
    }

    private static string? Fact(ImmutableArray<RewardTrackFactSnapshot> facts, string key)
    {
        if (facts.IsDefaultOrEmpty)
        {
            return null;
        }

        foreach (RewardTrackFactSnapshot fact in facts)
        {
            if (string.Equals(fact.Key, key, StringComparison.Ordinal))
            {
                return fact.Value;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether a comma-separated list holds this value. Entries are trimmed, because an operator
    /// typing "4312, 4313" means two ids and not one of them followed by a space.
    /// </summary>
    private static bool ListContains(string list, ReadOnlySpan<char> value)
    {
        foreach (Range range in list.AsSpan().Split(','))
        {
            if (list.AsSpan()[range].Trim().SequenceEqual(value))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether two comma-separated lists share at least one entry.</summary>
    /// <remarks>
    /// Quadratic, and deliberately: both sides are short — a look carries at most a dozen figure
    /// sets and an operator picks a handful of garments — and this runs behind the interest gate on
    /// the room's event path, where allocating a set would cost more than the scan it saves.
    /// </remarks>
    private static bool ListsIntersect(string list, string other)
    {
        foreach (Range range in other.AsSpan().Split(','))
        {
            ReadOnlySpan<char> entry = other.AsSpan()[range].Trim();

            if (entry.Length > 0 && ListContains(list, entry))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The sign of <paramref name="actual"/> against <paramref name="expected"/>, or null when
    /// either is not a whole number.
    /// </summary>
    /// <remarks>
    /// Null rather than a guess, and the caller turns it into a failed filter. Facts are strings by
    /// design, so a numeric operator can always be pointed at something that is not a number — an
    /// operator moving a filter from <c>price</c> to <c>room_name</c> and leaving the operator
    /// behind. Treating that as "0, and therefore at least 0" would make the step pass for every
    /// room in the hotel.
    /// </remarks>
    private static int? Compare(string actual, string expected) =>
        long.TryParse(actual, NumberStyles.Integer, CultureInfo.InvariantCulture, out long left)
        && long.TryParse(
            expected,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out long right
        )
            ? left.CompareTo(right)
            : null;

    private static IEnumerable<string> Split(string keys) =>
        keys.Length == 0
            ? []
            : keys.Split(DistinctSeparator, StringSplitOptions.RemoveEmptyEntries);
}
