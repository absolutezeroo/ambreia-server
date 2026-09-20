using System.Collections.Immutable;
using Orleans;

namespace Vortex.Primitives.RewardTracks.Snapshots;

/// <summary>
/// One node of a step's condition tree.
/// </summary>
/// <remarks>
/// <para>
/// One record for all four kinds rather than a hierarchy: this is walked on the room's event path
/// behind content an operator typed, and a shape the serializer and the evaluator can both read
/// without a type switch costs less than the tidiness of four records would buy.
/// </para>
/// <para>
/// A <see cref="PredicateKind.Test"/> carries <see cref="Test"/> and no children; the other three
/// carry children and no test. Anything else is content that should have been refused when it was
/// written, and the evaluator reads it as false rather than throwing — a campaign that stops the
/// pipeline is worse than a task that does not advance.
/// </para>
/// </remarks>
[GenerateSerializer, Immutable]
public sealed record StepPredicateSnapshot
{
    [Id(0)]
    public required PredicateKind Kind { get; init; }

    /// <summary>The comparison, for <see cref="PredicateKind.Test"/> only.</summary>
    [Id(1)]
    public RewardTrackStepFilterSnapshot? Test { get; init; }

    /// <summary>The operands, for every kind but <see cref="PredicateKind.Test"/>.</summary>
    [Id(2)]
    public ImmutableArray<StepPredicateSnapshot> Children { get; init; } = [];
}
