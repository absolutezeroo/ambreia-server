using System;
using System.Collections.Generic;
using System.Globalization;
using Vortex.Primitives.RewardTracks;
using Vortex.Primitives.RewardTracks.Admin;
using Vortex.Primitives.Signals;

namespace Vortex.Dashboard.API.Admin.Rules;

/// <summary>
/// Which sequence an operator is allowed to save.
/// </summary>
/// <remarks>
/// <para>
/// Everything refused here fails silently if it is allowed through: a task whose step can never be
/// satisfied does not error, it just stops advancing, and nobody can tell that apart from "no one
/// has done it yet". So the editor refuses it while the operator is still looking at the form.
/// </para>
/// <para>
/// The rule that earns its keep is the fact check. <c>place_item</c> emits no <c>player</c>, so a
/// step filtering it on one is a dead task — and there is nothing on any screen that would say so.
/// <c>ISignalVocabulary</c> is the list, and it is also what the dashboard offers, so
/// the two cannot disagree.
/// </para>
/// </remarks>
internal static class RewardTrackSequenceRules
{
    /// <summary>
    /// The first thing wrong with a sequence, as an error code, or <c>null</c> when it is saveable.
    /// One problem rather than all of them: the form shows one message.
    /// </summary>
    public static string? FirstProblem(
        IReadOnlyList<RewardTrackTaskStepSpec>? steps,
        ISignalVocabulary vocabulary
    )
    {
        if (steps is null || steps.Count == 0)
        {
            // A task with no steps is legal on the way in: the catalog builds one from the task's
            // own action. Only an explicitly empty list is a mistake worth naming.
            return steps is null ? null : "sequence_needs_a_step";
        }

        for (int index = 0; index < steps.Count; index++)
        {
            RewardTrackTaskStepSpec step = steps[index];

            if (string.IsNullOrWhiteSpace(step.ActionCode))
            {
                return "step_action_required";
            }

            // The two spellings of the same conditions. A step that sends both means the arranged
            // one: that is what the engine reads, so it is what has to be refused or allowed.
            if (step.Tree is { Count: > 0 })
            {
                if (NodesProblem(steps, index, step, step.Tree, vocabulary) is string nested)
                {
                    return nested;
                }

                continue;
            }

            foreach (RewardTrackStepFilterSpec filter in step.Filters ?? [])
            {
                if (Problem(steps, index, step, filter, vocabulary) is string problem)
                {
                    return problem;
                }
            }
        }

        return null;
    }

    private static string? NodesProblem(
        IReadOnlyList<RewardTrackTaskStepSpec> steps,
        int index,
        RewardTrackTaskStepSpec step,
        IReadOnlyList<RewardTrackStepNodeSpec> nodes,
        ISignalVocabulary vocabulary
    )
    {
        foreach (RewardTrackStepNodeSpec node in nodes)
        {
            if (NodeProblem(steps, index, step, node, vocabulary) is string problem)
            {
                return problem;
            }
        }

        return null;
    }

    /// <summary>
    /// What is wrong with one node of a condition tree, its own shape first and then its children.
    /// </summary>
    /// <remarks>
    /// The shape rules exist because the engine reads a malformed node as false rather than
    /// throwing — the right call on the room's event path, and the reason a bad shape would
    /// otherwise be a task that saves, shows nothing, and never advances.
    /// </remarks>
    private static string? NodeProblem(
        IReadOnlyList<RewardTrackTaskStepSpec> steps,
        int index,
        RewardTrackTaskStepSpec step,
        RewardTrackStepNodeSpec node,
        ISignalVocabulary vocabulary
    )
    {
        if (!Enum.IsDefined(node.Kind))
        {
            return "predicate_shape_mismatch";
        }

        if (node.Kind == PredicateKind.Test)
        {
            // A test carries a test and nothing else. Children hanging off one are read by nobody,
            // which is worse than being refused: they look saved.
            return node.Children is { Count: > 0 } ? "predicate_shape_mismatch"
                : node.Test is null ? "filter_incomplete"
                : Problem(steps, index, step, node.Test, vocabulary);
        }

        if (node.Test is not null)
        {
            return "predicate_shape_mismatch";
        }

        int children = node.Children?.Count ?? 0;

        // Asked before the empty check so an empty negation is told what a negation needs, rather
        // than the less useful half of the truth.
        if (node.Kind == PredicateKind.Not && children != 1)
        {
            return "predicate_not_needs_one_child";
        }

        if (children == 0)
        {
            // An empty group is not "no conditions": an empty Any is satisfied by nothing, and an
            // empty All under one makes its whole branch true without saying anything.
            return "predicate_group_needs_a_child";
        }

        return NodesProblem(steps, index, step, node.Children!, vocabulary);
    }

    private static string? Problem(
        IReadOnlyList<RewardTrackTaskStepSpec> steps,
        int index,
        RewardTrackTaskStepSpec step,
        RewardTrackStepFilterSpec filter,
        ISignalVocabulary vocabulary
    )
    {
        // A filter that names either side is judged as two values. The path below is the one every
        // filter stored today takes, and it is left exactly as it was: nothing that saves this
        // afternoon may stop saving because a newer shape exists.
        if (filter.Left is not null || filter.Right is not null)
        {
            return SidesProblem(steps, index, step, filter, vocabulary);
        }

        string value = filter.Value?.Trim() ?? string.Empty;

        if (value.Length == 0 || string.IsNullOrWhiteSpace(filter.FactKey))
        {
            return "filter_incomplete";
        }

        FactKey? fact = vocabulary.FactFor(step.ActionCode, filter.FactKey);

        // An operator that says nothing about this kind of fact: Contains on a room id, or an exact
        // match on a line a player typed. The engine evaluates both happily and matches neither, so
        // the only place to catch it is here, while the operator is still looking at the form.
        if (fact is not null && !FactOperators.Allows(fact.Kind, (int)filter.Operator))
        {
            return "filter_operator_not_valid_for_fact";
        }

        if (!vocabulary.Emits(step.ActionCode, filter.FactKey))
        {
            // The step's own action never reports this. Nothing would ever satisfy the filter.
            return "filter_fact_not_emitted_by_action";
        }

        int referenced = BackReference(value);

        if (referenced < 0)
        {
            if (IsList(filter.Operator) && !HasTwoOrMoreEntries(value))
            {
                return "filter_one_of_needs_a_list";
            }

            // A numeric operator over something that is not a number can never be true. The kind
            // check above already refuses the wrong fact; this refuses the wrong literal, which is
            // the same mistake made in the other box of the same row.
            if (IsNumeric(filter.Operator) && !IsWholeNumber(value))
            {
                return "filter_needs_a_number";
            }

            // Garments are picked, never typed, so a value here that is not a list of set ids came
            // from somewhere other than the picker -- the operations API, or an edit by hand.
            return fact?.Kind == FactKind.FigureSetId && !IsWholeNumberList(value)
                ? "filter_needs_a_number"
                : null;
        }

        if (referenced >= index)
        {
            // Pointing at itself or forwards. The step it names has not run when this one is tested.
            return "filter_reference_must_be_earlier";
        }

        return vocabulary.Emits(steps[referenced].ActionCode, filter.FactKey)
            ? null
            // The earlier step never recorded this fact, so there is nothing for $N to resolve to.
            // "The same furniture" only works between two steps that both talk about furniture.
            : "filter_reference_fact_not_captured";
    }

    /// <summary>
    /// What is wrong with a comparison that names its sides.
    /// </summary>
    /// <remarks>
    /// Both sides get the same treatment, which is the point of them being the same thing. The
    /// checks after that are about the pair: the operator has to suit what is on the left, a typed
    /// value has to be the shape the operator reads, and two facts have to be about the same kind
    /// of thing — a room id compared with a player id evaluates cleanly and matches nothing, for
    /// ever, with nothing on any screen to say so.
    /// </remarks>
    private static string? SidesProblem(
        IReadOnlyList<RewardTrackTaskStepSpec> steps,
        int index,
        RewardTrackTaskStepSpec step,
        RewardTrackStepFilterSpec filter,
        ISignalVocabulary vocabulary
    )
    {
        RewardTrackStepValueSpec left =
            filter.Left ?? new RewardTrackStepValueSpec(StepValueKind.Fact, filter.FactKey ?? "");
        RewardTrackStepValueSpec right =
            filter.Right ?? new RewardTrackStepValueSpec(StepValueKind.Literal, filter.Value ?? "");

        if (SideProblem(steps, index, step, left, vocabulary) is string bad)
        {
            return bad;
        }

        if (SideProblem(steps, index, step, right, vocabulary) is string alsoBad)
        {
            return alsoBad;
        }

        FactKind? leftKind = KindOf(steps, step, left, vocabulary);

        if (leftKind is FactKind kind && !FactOperators.Allows(kind, (int)filter.Operator))
        {
            return "filter_operator_not_valid_for_fact";
        }

        // Only a typed value can be the wrong shape. A fact's shape is the vocabulary's business,
        // and the kind check above is where that is settled.
        if (right.Kind == StepValueKind.Literal)
        {
            string typed = right.Arg.Trim();

            if (IsList(filter.Operator) && !HasTwoOrMoreEntries(typed))
            {
                return "filter_one_of_needs_a_list";
            }

            if (IsNumeric(filter.Operator) && !IsWholeNumber(typed))
            {
                return "filter_needs_a_number";
            }

            if (leftKind == FactKind.FigureSetId && !IsWholeNumberList(typed))
            {
                return "filter_needs_a_number";
            }
        }

        return
            KindOf(steps, step, right, vocabulary) is FactKind rightKind
            && leftKind is FactKind bothKnown
            && rightKind != bothKnown
            ? "filter_sides_do_not_compare"
            : null;
    }

    /// <summary>What is wrong with one side on its own.</summary>
    private static string? SideProblem(
        IReadOnlyList<RewardTrackTaskStepSpec> steps,
        int index,
        RewardTrackTaskStepSpec step,
        RewardTrackStepValueSpec value,
        ISignalVocabulary vocabulary
    )
    {
        if (value.Kind == StepValueKind.Literal)
        {
            return value.Arg.Trim().Length == 0 ? "filter_incomplete" : null;
        }

        if (string.IsNullOrWhiteSpace(value.Arg))
        {
            return "filter_incomplete";
        }

        switch (value.Kind)
        {
            case StepValueKind.Fact:
            case StepValueKind.CountOf:
                // The step's own action never reports this, so nothing would ever satisfy it.
                return vocabulary.Emits(step.ActionCode, value.Arg)
                    ? null
                    : "filter_fact_not_emitted_by_action";

            case StepValueKind.StepFact:
                if (value.Step < 0 || value.Step >= index)
                {
                    // Pointing at itself or forwards: that step has not run when this one is
                    // tested.
                    return "filter_reference_must_be_earlier";
                }

                // The earlier step never recorded this, so there is nothing to resolve to.
                return vocabulary.Emits(steps[value.Step].ActionCode, value.Arg)
                    ? null
                    : "filter_reference_fact_not_captured";

            default:
                // A kind from a newer build, or from a hand-written call. The engine reads it as
                // nothing, which is a task that never advances.
                return "filter_value_not_supported";
        }
    }

    /// <summary>
    /// What kind of thing a side is worth, or <c>null</c> when nothing can say.
    /// </summary>
    /// <remarks>
    /// A typed value has no kind — it is whatever the other side needs it to be, and the shape
    /// checks above are what hold it to that.
    /// </remarks>
    private static FactKind? KindOf(
        IReadOnlyList<RewardTrackTaskStepSpec> steps,
        RewardTrackTaskStepSpec step,
        RewardTrackStepValueSpec value,
        ISignalVocabulary vocabulary
    ) =>
        value.Kind switch
        {
            StepValueKind.Fact => vocabulary.FactFor(step.ActionCode, value.Arg)?.Kind,
            StepValueKind.StepFact => value.Step >= 0 && value.Step < steps.Count
                ? vocabulary.FactFor(steps[value.Step].ActionCode, value.Arg)?.Kind
                : null,
            // However long the list is, how many of them there are is a number.
            StepValueKind.CountOf => FactKind.Number,
            _ => null,
        };

    /// <summary>The step a <c>$N</c> value points at, or <c>-1</c> when the value is a literal.</summary>
    public static int BackReference(string value) =>
        value.Length > 1
        && value[0] == '$'
        && int.TryParse(
            value.AsSpan(1),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int step
        )
            ? step
            : -1;

    private static bool IsList(StepFilterOperator op) =>
        op is StepFilterOperator.OneOf or StepFilterOperator.NotOneOf;

    private static bool IsNumeric(StepFilterOperator op) =>
        op is StepFilterOperator.AtLeast or StepFilterOperator.AtMost;

    private static bool IsWholeNumber(string value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);

    private static bool IsWholeNumberList(string value)
    {
        foreach (string part in value.Split(','))
        {
            if (!IsWholeNumber(part.Trim()))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasTwoOrMoreEntries(string value)
    {
        int entries = 0;

        foreach (string part in value.Split(','))
        {
            if (part.Trim().Length > 0)
            {
                entries++;
            }
        }

        return entries >= 2;
    }
}
