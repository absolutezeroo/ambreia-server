using Orleans;

namespace Vortex.Primitives.RewardTracks.Snapshots;

/// <summary>
/// One side of a comparison.
/// </summary>
/// <remarks>
/// <para>
/// Flat on purpose: a kind and its argument, never a value holding another value. "How many
/// garments" is the kind <see cref="StepValueKind.CountOf"/> over a fact key, not a composition of
/// a counting value with a fact value. That keeps the hot path a switch rather than a second
/// recursion, and it keeps the editor a dropdown rather than a nested drop target.
/// </para>
/// <para>
/// The day something genuinely needs composing — <c>a + b</c>, or a count of a count — this grows
/// a child. Nothing today does, and a general expression tree built for nothing is a general
/// expression tree nobody can validate.
/// </para>
/// </remarks>
[GenerateSerializer, Immutable]
public sealed record StepValueSnapshot
{
    [Id(0)]
    public required StepValueKind Kind { get; init; }

    /// <summary>
    /// A fact key for <see cref="StepValueKind.Fact"/>, <see cref="StepValueKind.StepFact"/> and
    /// <see cref="StepValueKind.CountOf"/>; the text itself for
    /// <see cref="StepValueKind.Literal"/>.
    /// </summary>
    [Id(1)]
    public required string Arg { get; init; }

    /// <summary>
    /// Which earlier step to read, for <see cref="StepValueKind.StepFact"/>. <c>-1</c> otherwise.
    /// </summary>
    /// <remarks>
    /// Parsed once by the catalog, like the back-reference it generalises: the hot path never
    /// re-reads a string to find a number.
    /// </remarks>
    [Id(2)]
    public int Step { get; init; } = -1;

    /// <summary>What the left side of every test written before this existed means.</summary>
    public static StepValueSnapshot OfFact(string factKey) =>
        new() { Kind = StepValueKind.Fact, Arg = factKey };

    /// <summary>And what the right side means.</summary>
    public static StepValueSnapshot OfLiteral(string text) =>
        new() { Kind = StepValueKind.Literal, Arg = text };

    /// <summary>What <c>$N</c> means: that step's value for this same fact key.</summary>
    public static StepValueSnapshot OfStep(int step, string factKey) =>
        new()
        {
            Kind = StepValueKind.StepFact,
            Arg = factKey,
            Step = step,
        };
}
